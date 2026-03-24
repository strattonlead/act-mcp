using ACT.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ACT.Services;

public interface IAutoEvaluationService
{
    Task EvaluateConversationAsync(Guid conversationId);
    bool IsProcessing(Guid conversationId);
    (string Status, int Progress) GetProcessingStatus(Guid conversationId);
    event Action<Guid>? OnProcessingStateChanged;
}

public class AutoEvaluationService : IAutoEvaluationService
{
    private readonly IConversationService _conversationService;
    private readonly IFileRepository _fileRepository;
    private readonly IS3Service _s3Service;
    private readonly IFileParsingService _fileParsingService;
    private readonly IChatAgent _chatAgent;
    private readonly IActService _actService;
    private readonly IActProcessingService _actProcessingService;
    private readonly ILogger<AutoEvaluationService> _logger;

    // Track processing state: ConversationId -> (Status, Progress)
    private readonly ConcurrentDictionary<Guid, (string Status, int Progress)> _processingStates = new();

    public event Action<Guid> OnProcessingStateChanged;

    public AutoEvaluationService(
        IConversationService conversationService,
        IFileRepository fileRepository,
        IS3Service s3Service,
        IFileParsingService fileParsingService,
        IChatAgent chatAgent,
        IActService actService,
        IActProcessingService actProcessingService,
        ILogger<AutoEvaluationService> logger)
    {
        _conversationService = conversationService;
        _fileRepository = fileRepository;
        _s3Service = s3Service;
        _fileParsingService = fileParsingService;
        _chatAgent = chatAgent;
        _actService = actService;
        _actProcessingService = actProcessingService;
        _logger = logger;
    }

    public bool IsProcessing(Guid conversationId) => _processingStates.ContainsKey(conversationId);

    public (string Status, int Progress) GetProcessingStatus(Guid conversationId)
    {
        return _processingStates.TryGetValue(conversationId, out var status)
            ? status
            : ("Idle", 0);
    }

    public async Task EvaluateConversationAsync(Guid conversationId)
    {
        if (_processingStates.ContainsKey(conversationId)) return;

        try
        {
            UpdateState(conversationId, "Starting...", 0);

            // 1. Get Conversation and Attached File
            var conversation = await _conversationService.GetByIdAsync(conversationId);
            if (conversation == null) throw new Exception("Conversation not found");
            if (!conversation.AttachedFileId.HasValue) throw new Exception("No attached file");

            var uploadedFile = await _fileRepository.GetByIdAsync(conversation.AttachedFileId.Value);
            if (uploadedFile == null) throw new Exception("Attached file record not found");

            // 2. Get Dictionary Data
            if (string.IsNullOrEmpty(conversation.DictionaryKey)) throw new Exception("Dictionary not selected");

            var availableIdentities = await _actService.GetDictionaryIdentitiesAsync(conversation.DictionaryKey);
            var availableBehaviors = await _actService.GetDictionaryBehaviorsAsync(conversation.DictionaryKey);

            // 3. Download and Extract Text
            UpdateState(conversationId, "Downloading file...", 10);
            using var stream = await _s3Service.GetFileStreamAsync(uploadedFile.S3BucketName, uploadedFile.S3Key);

            UpdateState(conversationId, "Extracting text...", 20);
            var extractedText = await _fileParsingService.ExtractTextAsync(stream, uploadedFile.FileName);

            // 4. Parse Messages
            UpdateState(conversationId, "Parsing messages...", 30);
            var parsedMessages = await _chatAgent.ParseMessagesAsync(extractedText);

            // 5. Label Identities
            UpdateState(conversationId, "Labeling identities...", 50);
            var labeledSpeakers = await _chatAgent.LabelIdentitiesAsync(parsedMessages, availableIdentities);
            var speakerMap = labeledSpeakers.ToDictionary(s => s.OriginalLabel, s => s);

            // Add Persons to Conversation
            await _conversationService.ClearPersonsAsync(conversation.Id);
            // NOTE: ClearPersonsAsync might not exist or we might want to append? 
            // For auto-eval, usually we assume it populates an empty conversation.
            // If we are strictly adding, we should check duplicates.
            // Let's assume we are adding new ones or updating if they exist.

            foreach (var speaker in labeledSpeakers)
            {
                // Simple check to avoid duplicates if re-running
                if (!conversation.Persons.Any(p => p.Name == speaker.DisplayName))
                {
                    await _conversationService.AddPersonAsync(conversation.Id, new Person
                    {
                        Name = speaker.DisplayName,
                        Identity = speaker.Identity,
                        Gender = "average"
                    });
                }
            }

            // 6. Detect Situations
            UpdateState(conversationId, "Detecting situations...", 70);
            var detectedSituations = await _chatAgent.DetectSituationsAsync(parsedMessages, labeledSpeakers, availableBehaviors);

            // 7. Calculate and Save
            UpdateState(conversationId, "Calculating events...", 90);

            // We might want to clear existing situations if this is a "re-run"? 
            // Logic: If user clicks Auto Eval, we probably assume a fresh start or appending.
            // Let's append but user should probably clear manually if they want fresh.

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
                                Gender = "average"
                            },
                            Object = new Person
                            {
                                Name = objectSpeaker.DisplayName,
                                Identity = objectSpeaker.Identity,
                                Gender = "average"
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

            UpdateState(conversationId, "Completed", 100);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Evaluation failed for conversation {conversationId}");
            UpdateState(conversationId, $"Failed: {ex.Message}", 0);
        }
        finally
        {
            // Keep the final state for a moment or clear it?
            // If we clear it immediately, UI might miss the "Completed" state.
            // Let's keep it in "Completed" state, UI can clear it when user dismisses or after timeout?
            // For now, let's just leave it. If user reloads, it's gone from memory.

            // Actually, we should probably remove it after a delay so the UI knows it's done. 
            // Or better: Use "Processing" vs "Done".
            // Implementation: Remove from dictionary after short delay so UI returns to idle?

            await Task.Delay(2000); // Show 100% for 2s
            _processingStates.TryRemove(conversationId, out _);
            OnProcessingStateChanged?.Invoke(conversationId);
        }
    }

    private void UpdateState(Guid conversationId, string status, int progress)
    {
        _processingStates[conversationId] = (status, progress);
        OnProcessingStateChanged?.Invoke(conversationId);
    }
}
