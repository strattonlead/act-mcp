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

        const int MaxAttempts = 3;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                UpdateState(conversationId, attempt > 1 ? $"Retrying (attempt {attempt}/{MaxAttempts})..." : "Starting...", 0);

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

                // Determine if this is a re-eval with existing data we can reuse
                bool hasExistingTurns = conversation.ParsedMessages.Any();

                // Clear previous situations if re-running (but keep persons and parsed messages)
                if (conversation.Situations.Any())
                {
                    UpdateState(conversationId, "Clearing previous situations...", 25);
                    await _conversationService.ClearSituationsAsync(conversationId);
                }

                List<ParsedMessage> parsedMessages;

                if (hasExistingTurns)
                {
                    // Re-eval: reuse existing parsed messages and raw text
                    _logger.LogInformation("Re-eval: reusing {Count} existing parsed turns.", conversation.ParsedMessages.Count);
                    parsedMessages = conversation.ParsedMessages;
                    UpdateState(conversationId, $"Reusing {parsedMessages.Count} existing turns...", 30);
                }
                else
                {
                    // First eval: download, extract, and parse
                    UpdateState(conversationId, "Downloading file...", 10);
                    using var stream = await _s3Service.GetFileStreamAsync(uploadedFile.S3BucketName, uploadedFile.S3Key);

                    UpdateState(conversationId, "Extracting text...", 20);
                    var extractedText = await _fileParsingService.ExtractTextAsync(stream, uploadedFile.FileName);

                    UpdateState(conversationId, "Parsing messages...", 30);
                    parsedMessages = await _chatAgent.ParseMessagesAsync(extractedText);

                    // Store raw text, parsed messages, and total turns
                    conversation.RawText = extractedText;
                    conversation.ParsedMessages = parsedMessages;
                    conversation.TotalTurns = parsedMessages.Count;
                    await _conversationService.UpdateAsync(conversation);
                }

                // 5. Label Identities — reuse existing persons on re-eval
                UpdateState(conversationId, "Labeling identities...", 50);
                bool isReEval = conversation.Persons.Any();
                List<LabeledSpeaker> labeledSpeakers = new();

                if (isReEval)
                {
                    // Build a mapping from identity → original speaker label using parsed messages
                    // The parsed messages have original labels (e.g., "Bot", "User") while persons
                    // have display names (e.g., "Assistant", "Student") and identities.
                    var uniqueSpeakers = parsedMessages.Select(m => m.Speaker).Distinct().ToList();

                    labeledSpeakers = conversation.Persons.Select(p =>
                    {
                        // Try to find the original speaker label that maps to this person
                        // Match by: display name, identity, or positional fallback
                        var originalLabel = uniqueSpeakers.FirstOrDefault(s =>
                            s.Equals(p.Name, StringComparison.OrdinalIgnoreCase) ||
                            s.Equals(p.Identity, StringComparison.OrdinalIgnoreCase)) ?? p.Name;

                        return new LabeledSpeaker
                        {
                            OriginalLabel = originalLabel,
                            Identity = p.Identity,
                            DisplayName = p.Name
                        };
                    }).ToList();

                    // If no match found by name/identity, do positional mapping (first speaker → first person, etc.)
                    var unmappedSpeakers = uniqueSpeakers
                        .Where(s => !labeledSpeakers.Any(ls => ls.OriginalLabel.Equals(s, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    var unmappedPersons = labeledSpeakers
                        .Where(ls => !uniqueSpeakers.Any(s => s.Equals(ls.OriginalLabel, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    for (int idx = 0; idx < Math.Min(unmappedSpeakers.Count, unmappedPersons.Count); idx++)
                    {
                        unmappedPersons[idx].OriginalLabel = unmappedSpeakers[idx];
                    }

                    _logger.LogInformation("Re-eval: reusing {Count} existing persons. Speaker mapping: {Map}",
                        labeledSpeakers.Count,
                        string.Join(", ", labeledSpeakers.Select(ls => $"{ls.OriginalLabel}→{ls.DisplayName}")));

                    // Check if all speakers from parsed messages are covered
                    var coveredSpeakers = new HashSet<string>(labeledSpeakers.Select(ls => ls.OriginalLabel), StringComparer.OrdinalIgnoreCase);
                    var missingSpeakers = uniqueSpeakers.Where(s => !coveredSpeakers.Contains(s)).ToList();

                    if (missingSpeakers.Any())
                    {
                        _logger.LogWarning("Re-eval: {Count} speakers not mapped to persons: [{Missing}]. Running full identity labeling.",
                            missingSpeakers.Count, string.Join(", ", missingSpeakers));
                        isReEval = false; // Force full labeling below
                    }
                }

                if (!isReEval)
                {
                    labeledSpeakers = await _chatAgent.LabelIdentitiesAsync(parsedMessages, availableIdentities);
                    foreach (var speaker in labeledSpeakers)
                    {
                        await _conversationService.AddPersonAsync(conversation.Id, new Person
                        {
                            Name = speaker.DisplayName,
                            Identity = speaker.Identity,
                            Gender = "average"
                        });
                    }
                }

                var speakerMap = new Dictionary<string, LabeledSpeaker>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in labeledSpeakers)
                {
                    speakerMap.TryAdd(s.OriginalLabel, s);
                    speakerMap.TryAdd(s.Identity, s);
                    speakerMap.TryAdd(s.DisplayName, s);
                }

                // 6. Detect Situations
                UpdateState(conversationId, "Detecting situations...", 70);
                var detectedSituations = await _chatAgent.DetectSituationsAsync(parsedMessages, labeledSpeakers, availableBehaviors);

                // 7. Calculate and Save — track processed turns
                UpdateState(conversationId, "Calculating events...", 90);
                int processedTurns = 0;

                foreach (var detSit in detectedSituations)
                {
                    var situation = await _conversationService.AddSituationAsync(conversation.Id, detSit.Name);

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
                        }
                        catch (Exception iex)
                        {
                            _logger.LogWarning(iex, $"Failed to calculate EPA for event: {evt.Behavior}");
                        }
                    }
                }

                // Mark eval status based on turn coverage
                var finalConv = await _conversationService.GetByIdAsync(conversationId);
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

                UpdateState(conversationId, $"Completed ({processedTurns}/{parsedMessages.Count} turns)", 100);
                return; // Success — exit the retry loop
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Evaluation failed for conversation {ConversationId} (attempt {Attempt}/{Max}).",
                    conversationId, attempt, MaxAttempts);

                if (attempt < MaxAttempts)
                {
                    _logger.LogInformation("Retrying evaluation for conversation {ConversationId}...", conversationId);
                    UpdateState(conversationId, $"Failed (attempt {attempt}/{MaxAttempts}), retrying...", 0);
                    continue;
                }

                // Final attempt failed — mark as failed
                try
                {
                    var failedConv = await _conversationService.GetByIdAsync(conversationId);
                    if (failedConv != null)
                    {
                        failedConv.AutoEvalStatus = $"Failed: {ex.Message}";
                        failedConv.AutoEvalAt = DateTime.UtcNow;
                        await _conversationService.UpdateAsync(failedConv);
                    }
                }
                catch { /* don't mask original error */ }

                UpdateState(conversationId, $"Failed: {ex.Message}", 0);
            }
        }

        // Cleanup: remove processing state after a short delay so UI sees the final status
        await Task.Delay(2000);
        _processingStates.TryRemove(conversationId, out _);
        OnProcessingStateChanged?.Invoke(conversationId);
    }

    private void UpdateState(Guid conversationId, string status, int progress)
    {
        _processingStates[conversationId] = (status, progress);
        OnProcessingStateChanged?.Invoke(conversationId);
    }
}
