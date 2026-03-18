using ACT.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACT.Services;

public interface IBatchEvaluationService
{
    List<BatchFileStatus> GetFiles();
    Task AddFilesAsync(IReadOnlyList<IBrowserFile> files);
    Task StartProcessingAsync(string dictionaryKey, List<string>? forcedIdentities = null);
    event Action? OnChange;
}

public class BatchEvaluationService : IBatchEvaluationService
{
    private readonly IS3Service _s3Service;
    private readonly IFileParsingService _fileParsingService;
    private readonly IChatAgent _chatAgent;
    private readonly IConversationService _conversationService;
    private readonly IActService _actService;
    private readonly IActProcessingService _actProcessingService;
    private readonly IFileRepository _fileRepository;
    private readonly IBatchFileStatusService _fileStatusService;
    private readonly ILogger<BatchEvaluationService> _logger;

    // We need to temporarily hold streams or rely on the UI needed to upload them immediately?
    // IBrowserFile streams are fragile. 
    // Strategy: When AddFilesAsync is called, we create the status objects.
    // The actual streams must be processed BEFORE StartProcessingAsync if we want to decouple the UI interaction.
    // OR we ask the user to select files, and then we immediately read them into memory (if small) or temp files?
    // To safe memory, we will process them one by one?
    // User requested: "add multiple files ... and the programm evaluates them automatically"
    // "for every file a progress bar ... user can click ... updated in realtime"

    // Storing Streams in the service is risky if the circuit breaks?
    // Let's assume we handle the file content reading at the beginning of the process loop or eagerly.
    // For robust implementation given the constraints, I will hold a reference to IBrowserFile but be aware it might timeout if the process is long.
    // Better: Read to temporary file or MemoryStream upon "Add".

    private readonly Dictionary<Guid, (Stream Stream, string FileName, string ContentType)> _pendingStreams = new();

    public event Action? OnChange;

    public BatchEvaluationService(
        IS3Service s3Service,
        IFileParsingService fileParsingService,
        IChatAgent chatAgent,
        IConversationService conversationService,
        IActService actService,
        IActProcessingService actProcessingService,
        IFileRepository fileRepository,
        IBatchFileStatusService fileStatusService,
        ILogger<BatchEvaluationService> logger)
    {
        _s3Service = s3Service;
        _fileParsingService = fileParsingService;
        _chatAgent = chatAgent;
        _conversationService = conversationService;
        _actService = actService;
        _actProcessingService = actProcessingService;
        _fileRepository = fileRepository;
        _fileStatusService = fileStatusService;
        _logger = logger;
    }

    public List<BatchFileStatus> GetFiles() => _fileStatusService.GetAll();

    public async Task AddFilesAsync(IReadOnlyList<IBrowserFile> files)
    {
        foreach (var file in files)
        {

            var status = new BatchFileStatus
            {
                FileName = file.Name,
                ContentType = file.ContentType,
                Size = file.Size,
                State = BatchFileState.Pending
            };
            _fileStatusService.Add(status);

            // Copy to memory stream to avoid IBrowserFile timeout issues during later processing
            // Limit 10MB per file for safety
            var ms = new MemoryStream();
            await file.OpenReadStream(10 * 1024 * 1024).CopyToAsync(ms);
            ms.Position = 0;

            _pendingStreams[status.Id] = (ms, file.Name, file.ContentType);

            NotifyStateChanged();
        }
    }

    public async Task StartProcessingAsync(string dictionaryKey, List<string>? forcedIdentities = null)
    {
        // Get Dictionary Data first
        List<string> identities = new();
        List<string> behaviors = new();
        try
        {
            identities = await _actService.GetDictionaryIdentitiesAsync(dictionaryKey);
            behaviors = await _actService.GetDictionaryBehaviorsAsync(dictionaryKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dictionary data.");
            // Mark all as failed?
            return;
        }

        // Process files one by one (or parallel with Semaphore)
        // Sequential for now to be safe with LLM rate limits
        foreach (var fileStatus in _fileStatusService.GetAll().Where(f => f.State == BatchFileState.Pending).ToList())
        {
            if (!_pendingStreams.ContainsKey(fileStatus.Id)) continue;

            var (stream, fileName, contentType) = _pendingStreams[fileStatus.Id];

            try
            {
                // If forcedIdentities provided, explicitly use them. 
                // They should be validated against dictionary if strict, but per requirements "user can define ... only these".
                // We will pass the forced list as the available list to the Agent.
                // However, we should also probably validate they exist in the dictionary? 
                // Creating a hybrid list or just passing forced? 
                // Plan said: "Set availableIdentities to ONLY this list".

                var targetIdentities = (forcedIdentities != null && forcedIdentities.Any())
                    ? forcedIdentities
                    : identities;

                await ProcessFileAsync(fileStatus, stream, dictionaryKey, targetIdentities, behaviors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing file {fileStatus.FileName}");
                fileStatus.State = BatchFileState.Failed;
                fileStatus.ErrorMessage = ex.Message;
                NotifyStateChanged();
            }
            finally
            {
                // Cleanup stream
                stream.Dispose();
                _pendingStreams.Remove(fileStatus.Id);
            }
        }
    }

    private async Task ProcessFileAsync(
        BatchFileStatus status,
        Stream stream,
        string dictionaryKey,
        List<string> availableIdentities,
        List<string> availableBehaviors)
    {
        // 1. Upload to S3
        status.State = BatchFileState.Uploading;
        status.StatusMessage = "Uploading to S3...";
        status.Progress = 10;
        NotifyStateChanged();

        var uploaded = await _s3Service.UploadFileAsync(stream, status.FileName, status.ContentType);

        // 2. Parse Text
        status.State = BatchFileState.Parsing;
        status.StatusMessage = "Extracting text...";
        status.Progress = 20;
        NotifyStateChanged();

        status.ExtractedText = await _fileParsingService.ExtractTextAsync(stream, status.FileName);

        if (string.IsNullOrWhiteSpace(status.ExtractedText))
        {
            throw new Exception("No text could be extracted from the file.");
        }

        // 3. Create Conversation
        status.State = BatchFileState.CreatingConversation;
        status.StatusMessage = "Creating conversation...";
        status.Progress = 30;
        NotifyStateChanged();

        var conversation = await _conversationService.CreateAsync(
            Path.GetFileNameWithoutExtension(status.FileName),
            dictionaryKey);

        status.ConversationId = conversation.Id;

        // Link file to conversation
        conversation.AttachedFileId = uploaded.Id;
        await _conversationService.UpdateAsync(conversation);

        // 4. Analyze - Parse Messages
        status.State = BatchFileState.Analyzing;
        status.StatusMessage = "Parsing messages...";
        status.Progress = 40;
        NotifyStateChanged();

        var parsedMessages = await _chatAgent.ParseMessagesAsync(status.ExtractedText);

        // 5. Analyze - Label Identities
        status.StatusMessage = "Labeling identities...";
        status.Progress = 60;
        NotifyStateChanged();

        var labeledSpeakers = await _chatAgent.LabelIdentitiesAsync(parsedMessages, availableIdentities);
        var speakerMap = labeledSpeakers.ToDictionary(s => s.OriginalLabel, s => s);

        // Add Persons to Conversation
        foreach (var speaker in labeledSpeakers)
        {
            await _conversationService.AddPersonAsync(conversation.Id, new Person
            {
                Name = speaker.DisplayName,
                Identity = speaker.Identity,
                Gender = "avg" // Default
            });
        }

        // 6. Analyze - Detect Situations
        status.StatusMessage = "Detecting situations...";
        status.Progress = 80;
        NotifyStateChanged();

        var detectedSituations = await _chatAgent.DetectSituationsAsync(parsedMessages, labeledSpeakers, availableBehaviors);

        // 7. Calculate and Save
        status.StatusMessage = "Calculating EPA...";
        status.Progress = 90;
        NotifyStateChanged();

        foreach (var detSit in detectedSituations)
        {
            var situation = await _conversationService.AddSituationAsync(conversation.Id, detSit.Name);

            // Track the previous event's result for transient chaining within this situation.
            // Each situation starts fresh (null = use fundamentals for event 1).
            InteractionResult? previousResult = null;

            foreach (var evt in detSit.Events)
            {
                var actorSpeaker = speakerMap.GetValueOrDefault(evt.ActorSpeaker);
                var objectSpeaker = speakerMap.GetValueOrDefault(evt.ObjectSpeaker);

                if (actorSpeaker != null && objectSpeaker != null)
                {
                    var interaction = new Interaction
                    {
                        Actor = new Person
                        {
                            Name = actorSpeaker.DisplayName,
                            Identity = actorSpeaker.Identity,
                            Gender = "avg"
                        },
                        Object = new Person
                        {
                            Name = objectSpeaker.DisplayName,
                            Identity = objectSpeaker.Identity,
                            Gender = "avg"
                        },
                        Behavior = evt.Behavior
                    };

                    try
                    {
                        var result = await _actProcessingService.CalculateInteractionAsync(interaction, previousResult);
                        interaction.Result = result;
                        previousResult = result;

                        await _conversationService.AddEventAsync(conversation.Id, situation, interaction);
                    }
                    catch (Exception iex)
                    {
                        _logger.LogWarning(iex, $"Failed to calculate EPA for event: {evt.Behavior}");
                    }
                }
            }
        }

        status.State = BatchFileState.Completed;
        status.StatusMessage = "Done";
        status.Progress = 100;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => _fileStatusService.NotifyStateChanged();
}
