using ACT.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    private readonly LlmProviderConfig _llmConfig;

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
        LlmProviderConfig llmConfig,
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
        _llmConfig = llmConfig;
        _logger = logger;
    }

    public List<BatchFileStatus> GetFiles() => _fileStatusService.GetAll();

    public async Task AddFilesAsync(IReadOnlyList<IBrowserFile> files)
    {
        // Get existing conversation names to skip duplicates
        var existingNames = (await _conversationService.GetAllAsync())
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);

            // Skip if a conversation with this name already exists
            if (existingNames.Contains(nameWithoutExt))
            {
                var status = new BatchFileStatus
                {
                    FileName = file.Name,
                    ContentType = file.ContentType,
                    Size = file.Size,
                    State = BatchFileState.Skipped,
                    StatusMessage = "Conversation already exists"
                };
                _fileStatusService.Add(status);
                NotifyStateChanged();
                continue;
            }

            var fileStatus = new BatchFileStatus
            {
                FileName = file.Name,
                ContentType = file.ContentType,
                Size = file.Size,
                State = BatchFileState.Pending
            };
            _fileStatusService.Add(fileStatus);

            // Copy to memory stream to avoid IBrowserFile timeout issues during later processing
            // Limit 10MB per file for safety
            var ms = new MemoryStream();
            await file.OpenReadStream(10 * 1024 * 1024).CopyToAsync(ms);
            ms.Position = 0;

            _pendingStreams[fileStatus.Id] = (ms, file.Name, file.ContentType);

            // Track the name so subsequent files in the same batch don't duplicate either
            existingNames.Add(nameWithoutExt);

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

        // Sequential for local models (Ollama), parallel for cloud APIs
        int maxParallel = _llmConfig.IsLocalModel ? 1 : 20;
        _logger.LogInformation("Batch processing mode: {Mode} (max parallel: {Max})", _llmConfig.IsLocalModel ? "sequential" : "parallel", maxParallel);
        var semaphore = new SemaphoreSlim(maxParallel);

        var targetIdentities = (forcedIdentities != null && forcedIdentities.Any())
            ? forcedIdentities
            : identities;

        var pendingFiles = _fileStatusService.GetAll()
            .Where(f => f.State == BatchFileState.Pending && _pendingStreams.ContainsKey(f.Id))
            .ToList();

        var tasks = pendingFiles.Select(async fileStatus =>
        {
            await semaphore.WaitAsync();
            var (stream, fileName, contentType) = _pendingStreams[fileStatus.Id];
            try
            {
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
                stream.Dispose();
                _pendingStreams.Remove(fileStatus.Id);
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
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

        // Store raw text, parsed messages, and total turns
        conversation.RawText = status.ExtractedText;
        conversation.ParsedMessages = parsedMessages;
        conversation.TotalTurns = parsedMessages.Count;
        await _conversationService.UpdateAsync(conversation);

        // 5. Analyze - Label Identities
        status.StatusMessage = "Labeling identities...";
        status.Progress = 60;
        NotifyStateChanged();

        var labeledSpeakers = await _chatAgent.LabelIdentitiesAsync(parsedMessages, availableIdentities);
        // Build speaker map with multiple lookup keys: original label, identity, and display name
        var speakerMap = new Dictionary<string, LabeledSpeaker>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in labeledSpeakers)
        {
            speakerMap.TryAdd(s.OriginalLabel, s);
            speakerMap.TryAdd(s.Identity, s);
            speakerMap.TryAdd(s.DisplayName, s);
        }

        // Add Persons to Conversation
        foreach (var speaker in labeledSpeakers)
        {
            await _conversationService.AddPersonAsync(conversation.Id, new Person
            {
                Name = speaker.DisplayName,
                Identity = speaker.Identity,
                Gender = "average" // Default
            });
        }

        // 6. Analyze - Detect Situations
        status.StatusMessage = "Detecting situations...";
        status.Progress = 80;
        NotifyStateChanged();

        var detectedSituations = await _chatAgent.DetectSituationsAsync(parsedMessages, labeledSpeakers, availableBehaviors);

        // 7. Calculate and Save — track processed turns via originalMessage matching
        status.StatusMessage = "Calculating EPA...";
        status.Progress = 90;
        NotifyStateChanged();

        // Collect all original messages from parsed turns for matching
        var unmatchedTurns = new HashSet<int>(parsedMessages.Select(m => m.Order));
        int processedTurns = 0;

        foreach (var detSit in detectedSituations)
        {
            var situation = await _conversationService.AddSituationAsync(conversation.Id, detSit.Name);

            // Track transient EPAs by identity name for correct chaining.
            Dictionary<string, double[]>? transientsByIdentity = null;

            foreach (var evt in detSit.Events)
            {
                if (string.IsNullOrEmpty(evt.ActorSpeaker) || string.IsNullOrEmpty(evt.ObjectSpeaker))
                {
                    _logger.LogWarning("Skipping event '{Behavior}': null/empty speaker fields.", evt.Behavior);
                    continue;
                }
                var actorSpeaker = speakerMap.GetValueOrDefault(evt.ActorSpeaker);
                var objectSpeaker = speakerMap.GetValueOrDefault(evt.ObjectSpeaker);

                if (actorSpeaker == null || objectSpeaker == null)
                {
                    _logger.LogWarning("Skipping event '{Behavior}': speaker mapping failed. ActorSpeaker='{Actor}' (found={ActorFound}), ObjectSpeaker='{Object}' (found={ObjectFound}). Available keys: [{Keys}]",
                        evt.Behavior, evt.ActorSpeaker, actorSpeaker != null, evt.ObjectSpeaker, objectSpeaker != null,
                        string.Join(", ", speakerMap.Keys));
                    continue;
                }

                var interaction = new Interaction
                {
                    Actor = new Person
                    {
                        Name = actorSpeaker.DisplayName,
                        Identity = actorSpeaker.Identity,
                        Gender = "average"
                    },
                    Object = new Person
                    {
                        Name = objectSpeaker.DisplayName,
                        Identity = objectSpeaker.Identity,
                        Gender = "average"
                    },
                    Behavior = evt.Behavior,
                    OriginalMessage = evt.OriginalMessage
                };

                try
                {
                    var result = await _actProcessingService.CalculateInteractionAsync(interaction, transientsByIdentity);
                    interaction.Result = result;

                    transientsByIdentity = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        [interaction.Actor.Identity] = result.TransientActorEPA,
                        [interaction.Object.Identity] = result.TransientObjectEPA
                    };

                    await _conversationService.AddEventAsync(conversation.Id, situation, interaction);
                    processedTurns++;

                    // Try to match this event back to a parsed turn
                    if (!string.IsNullOrEmpty(evt.OriginalMessage))
                    {
                        var matchedTurn = parsedMessages.FirstOrDefault(m =>
                            unmatchedTurns.Contains(m.Order) &&
                            (m.Content.Equals(evt.OriginalMessage, StringComparison.OrdinalIgnoreCase) ||
                             m.Content.Contains(evt.OriginalMessage, StringComparison.OrdinalIgnoreCase) ||
                             evt.OriginalMessage.Contains(m.Content, StringComparison.OrdinalIgnoreCase)));
                        if (matchedTurn != null)
                            unmatchedTurns.Remove(matchedTurn.Order);
                    }
                }
                catch (Exception iex)
                {
                    _logger.LogWarning(iex, $"Failed to calculate EPA for event: {evt.Behavior}");
                }
            }
        }

        // 8. Update conversation with turn tracking and eval status
        var finalConv = await _conversationService.GetByIdAsync(conversation.Id);
        if (finalConv != null)
        {
            finalConv.TotalTurns = parsedMessages.Count;
            finalConv.ProcessedTurns = processedTurns;
            finalConv.AutoEvalAt = DateTime.UtcNow;

            if (parsedMessages.Count == 0)
                finalConv.AutoEvalStatus = "Failed: No turns parsed";
            else if (processedTurns == 0)
                finalConv.AutoEvalStatus = "Failed: No turns could be processed";
            else if (processedTurns == parsedMessages.Count)
                finalConv.AutoEvalStatus = "Success";
            else
                finalConv.AutoEvalStatus = $"Partial: {processedTurns}/{parsedMessages.Count} turns";

            await _conversationService.UpdateAsync(finalConv);
        }

        status.State = BatchFileState.Completed;
        status.StatusMessage = $"Done ({processedTurns}/{parsedMessages.Count} turns)";
        status.Progress = 100;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => _fileStatusService.NotifyStateChanged();
}
