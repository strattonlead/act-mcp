using ACT.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Services;

public class ChatAgent : IChatAgent
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<ChatAgent> _logger;

    /// <summary>Shared JSON options tolerant of common LLM quirks.</summary>
    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public ChatAgent(IChatClient chatClient, ILogger<ChatAgent> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> ExtractActEventsAsync(string conversationHistory, List<string>? availableBehaviors = null, CancellationToken ct = default)
    {
        var behaviorInstruction = availableBehaviors != null && availableBehaviors.Count > 0
            ? "The Behavior MUST be chosen from this allowed list:\n" + string.Join("\n", availableBehaviors.Select(b => $"  - {b}")) +
              "\nDo NOT invent behaviors. Use the EXACT term from the list."
            : "The Behavior must be mapped to a valid ACT behavior (e.g., 'advise', 'ask', 'greet').";

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System,
                "You are an expert in Affect Control Theory (ACT). Your task is to analyze a conversation and extract 'ACT events'. " +
                "An ACT event consists of an Actor, a Behavior, and an Object. " +
                "The Actor and Object must be mapped to valid identities (e.g., 'student', 'teacher', 'doctor'). " +
                behaviorInstruction + " " +
                "For the provided conversation, extract a list of events in the format: Actor|Behavior|Object. " +
                "Return ONLY the list of events, one per line. Do not include any other text."),
            new ChatMessage(ChatRole.User, conversationHistory)
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            return response.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract ACT events from LLM.");
            throw;
        }
    }

    public async Task<string> ExtractSingleBehaviorAsync(
        string text,
        List<string> availableBehaviors,
        string actorIdentity = "student",
        string objectIdentity = "assistant",
        CancellationToken ct = default)
    {
        if (availableBehaviors == null || availableBehaviors.Count == 0)
            throw new ArgumentException("availableBehaviors must not be empty", nameof(availableBehaviors));

        var validBehaviors = new HashSet<string>(availableBehaviors, StringComparer.OrdinalIgnoreCase);
        var behaviorListFormatted = string.Join(", ", availableBehaviors);

        var systemPrompt = $@"You are an expert in Affect Control Theory (ACT).
The {actorIdentity} is speaking to the {objectIdentity}. Pick the ONE behavior from the allowed list that best describes what the {actorIdentity} is doing socially in this message.

Think about what the speaker is DOING:
- Greeting → greet
- Asking a question → ask_about, query
- Giving advice/tips → advise, counsel
- Explaining → explain_something_to
- Agreeing → agree_with
- Thanking → thank
- Encouraging → encourage, comfort, reassure
- Requesting help → request_something_from, appeal_to
- Complaining → complain_to, criticize
- Apologizing → apologize_to
- Informing → inform, tell_something_to

ALLOWED BEHAVIORS: [{behaviorListFormatted}]

Respond with ONLY the behavior term, nothing else. Example: advise";

        const int maxRetries = 2;
        string behavior = availableBehaviors.Contains("acknowledge", StringComparer.OrdinalIgnoreCase)
            ? availableBehaviors.First(b => b.Equals("acknowledge", StringComparison.OrdinalIgnoreCase))
            : availableBehaviors[0];

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var chatMessages = new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, $"What behavior best describes THIS MESSAGE?\n\n[THIS MESSAGE] {actorIdentity}: {text}")
                };

                using var msgCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                msgCts.CancelAfter(TimeSpan.FromSeconds(60));

                var response = await _chatClient.GetResponseAsync(chatMessages, cancellationToken: msgCts.Token);
                var responseText = (response.Text ?? "").Trim();

                // Clean common LLM wrappers (quotes, brackets, JSON-ish fragments)
                responseText = responseText.Trim('"', '\'', '[', ']', '{', '}', ' ', '\n', '\r');
                if (responseText.Contains(':'))
                    responseText = responseText.Split(':').Last().Trim().Trim('"', '\'');
                if (responseText.Contains('\n'))
                    responseText = responseText.Split('\n')[0].Trim();

                if (validBehaviors.Contains(responseText))
                {
                    behavior = responseText;
                    break;
                }

                var corrected = TryFuzzyMatchBehavior(responseText, availableBehaviors);
                if (corrected != null)
                {
                    _logger.LogInformation("ExtractSingleBehavior: corrected '{Raw}' -> '{Corrected}'", responseText, corrected);
                    behavior = corrected;
                    break;
                }

                if (attempt == maxRetries - 1)
                {
                    behavior = ForceMatchBehavior(responseText, availableBehaviors);
                    _logger.LogWarning("ExtractSingleBehavior: force-matched '{Raw}' -> '{Forced}'", responseText, behavior);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("ExtractSingleBehavior: LLM timed out after 60s (attempt {Attempt}). Using fallback '{Behavior}'.", attempt + 1, behavior);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ExtractSingleBehavior: LLM call failed (attempt {Attempt}).", attempt + 1);
                if (attempt == maxRetries - 1)
                    _logger.LogError("ExtractSingleBehavior: giving up, using fallback '{Behavior}'.", behavior);
            }
        }

        return behavior;
    }

    public async Task<List<ParsedMessage>> ParseMessagesAsync(string rawInput, CancellationToken ct = default)
    {
        // Try deterministic parsing first for structured chat formats (Speaker: message)
        var deterministicResult = TryDeterministicParse(rawInput);
        if (deterministicResult != null && deterministicResult.Count >= 2)
        {
            _logger.LogInformation("Deterministic parser found {Count} turns — skipping LLM.", deterministicResult.Count);
            return deterministicResult;
        }

        var systemPrompt = @"You are an expert at parsing conversation transcripts.
Your task is to identify individual messages from a raw conversation and determine who the speaker is.

Rules:
- Look for patterns like 'User:', 'Bot:', 'A:', 'B:', or similar speaker indicators
- If no explicit labels exist, infer speakers from context (alternating pattern, etc.)
- Preserve the order of messages
- Handle various formats: chat logs, transcripts, dialogue scripts
- CRITICAL: Keep the EXACT original text in the content field. Do NOT translate, paraphrase, summarize, or modify the text in any way. Copy it character-for-character from the input.

Return a JSON array with this exact format (no other text):
[
  {""speaker"": ""<speaker_label>"", ""content"": ""<exact_original_message_text>"", ""order"": <0-indexed_position>}
]

Example input:
User: Hello
Bot: Hi there!

Example output:
[{""speaker"": ""User"", ""content"": ""Hello"", ""order"": 0}, {""speaker"": ""Bot"", ""content"": ""Hi there!"", ""order"": 1}]";

        var chatMessages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, rawInput)
        };

        const int MaxRetries = 3;
        for (int i = 0; i < MaxRetries; i++)
        {
            try
            {
                var response = await _chatClient.GetResponseAsync(chatMessages, cancellationToken: ct);
                var responseText = response.Text ?? "[]";
                var jsonText = SanitizeLlmJson(ExtractJson(responseText));

                var parsed = JsonSerializer.Deserialize<List<ParsedMessage>>(jsonText, LlmJsonOptions);

                return parsed ?? new List<ParsedMessage>();
            }
            catch (Exception ex) when (ex is JsonException)
            {
                _logger.LogWarning(ex, "Failed to parse messages JSON from LLM (Attempt {Retry}/{Max}).", i + 1, MaxRetries);
                if (i == MaxRetries - 1) throw;

                // Retry with explicit instruction to return only JSON
                chatMessages.Add(new ChatMessage(ChatRole.User,
                    "Your response was not valid JSON. Return ONLY a JSON array, no other text. " +
                    "Do not use literal newlines inside string values — use \\n instead. Example format:\n" +
                    @"[{""speaker"": ""User"", ""content"": ""Hello"", ""order"": 0}]"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse messages from LLM.");
                throw;
            }
        }

        return new List<ParsedMessage>();
    }

    public async Task<List<LabeledSpeaker>> LabelIdentitiesAsync(
        List<ParsedMessage> messages, 
        List<string> availableIdentities, 
        CancellationToken ct = default)
    {
        // Get unique speakers
        var uniqueSpeakers = messages.Select(m => m.Speaker).Distinct().ToList();
        
        // Use hash set for fast validation (case insensitive)
        var validIdentities = new HashSet<string>(availableIdentities, StringComparer.OrdinalIgnoreCase);

        var isForced = availableIdentities.Count < 20; // Heuristic: if small list provided, assume forced/strict subset
        var promptInstruction = isForced 
            ? $"You MUST strictly map the speakers to these SPECIFIC identities: {string.Join(", ", availableIdentities)}"
            : $"Available identities: {string.Join(", ", availableIdentities)}";

        var systemPrompt = $@"You are an expert in Affect Control Theory (ACT). 
Your task is to map conversation participants to ACT identity terms from a cultural dictionary.

{promptInstruction}

Important Rules:
1. You MUST choose an identity strictly from the provided list.
2. Do not invent new identities.
3. If an exact match is not found, choose the closest semantic match from the list.

Common mappings:
- 'Bot', 'Assistant', 'AI', 'System' → 'assistant' or similar helper role
- 'User', 'Human', 'Customer' → 'student', 'client', 'customer' or similar
- Named persons → infer from context

Speaker labels to map: {string.Join(", ", uniqueSpeakers)}

Return a JSON array with this exact format (no other text):
[
  {{""originalLabel"": ""<speaker_label>"", ""identity"": ""<act_identity>"", ""displayName"": ""<friendly_name>""}}
]

Choose identities that best represent the social role of each speaker in the conversation context.";

        var userContent = $"Map these speakers based on this conversation:\n\n" +
            string.Join("\n", messages.Select(m => $"{m.Speaker}: {m.Content}"));

        var chatMessages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userContent)
        };

        const int MaxRetries = 3;
        for (int i = 0; i < MaxRetries; i++)
        {
            try
            {
                var response = await _chatClient.GetResponseAsync(chatMessages, cancellationToken: ct);
                var responseText = response.Text ?? "[]";
                var jsonText = SanitizeLlmJson(ExtractJson(responseText));

                var labeled = JsonSerializer.Deserialize<List<LabeledSpeaker>>(jsonText, LlmJsonOptions) ?? new List<LabeledSpeaker>();

                // Validate identities
                var invalidEntries = new List<string>();
                foreach (var item in labeled)
                {
                    if (!validIdentities.Contains(item.Identity))
                    {
                        invalidEntries.Add($"Identity '{item.Identity}' for speaker '{item.OriginalLabel}' is NOT in the allowed list.");
                    }
                }

                if (invalidEntries.Count == 0)
                {
                    return labeled;
                }

                // If invalid, prepare for retry
                _logger.LogWarning("LLM returned invalid identities. Retrying ({RetryCount}/{MaxRetries}). Invalid: {Invalid}", i + 1, MaxRetries, string.Join(", ", invalidEntries));

                // Add the previous erroneous response and the error message to history
                chatMessages.Add(new ChatMessage(ChatRole.Assistant, responseText));
                chatMessages.Add(new ChatMessage(ChatRole.User, 
                    $"ERROR: The following identities are invalid: {string.Join("; ", invalidEntries)}. " +
                    $"You MUST choose valid identities from the provided list. Please correct this. return the full JSON again."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to label identities from LLM (Attempt {Retry}).", i + 1);
                if (i == MaxRetries - 1) throw;
            }
        }

        throw new Exception("Failed to obtain valid identities after retries.");
    }

    /// <summary>
    /// Per INTERACT guide: a "situation" is defined by participant identities, not topic changes.
    /// For fixed-identity conversations, there is ONE situation with all events.
    /// Processes each message individually with surrounding context for reliable behavior assignment.
    /// </summary>
    public async Task<List<DetectedSituation>> DetectSituationsAsync(
        List<ParsedMessage> messages,
        List<LabeledSpeaker> speakers,
        List<string> availableBehaviors,
        CancellationToken ct = default)
    {
        var validBehaviors = new HashSet<string>(availableBehaviors, StringComparer.OrdinalIgnoreCase);
        var behaviorListFormatted = string.Join(", ", availableBehaviors);
        var orderedMessages = messages.OrderBy(m => m.Order).ToList();
        var uniqueSpeakers = speakers.Select(s => s.OriginalLabel).ToList();

        // Build the identity context for the prompt
        var speakerContext = string.Join(", ", speakers.Select(s => $"{s.OriginalLabel} is a {s.Identity}"));

        // System prompt — stays the same for all messages
        var systemPrompt = $@"You are an expert in Affect Control Theory (ACT).
Given a message from a conversation, pick the ONE behavior from the allowed list that best describes what the speaker is doing socially.

Context: {speakerContext}

Think about what the speaker is DOING:
- Greeting → greet
- Asking a question → ask_about, query
- Giving advice/tips → advise, counsel
- Explaining → explain_something_to
- Agreeing → agree_with
- Thanking → thank
- Encouraging → encourage, comfort, reassure
- Requesting help → request_something_from, appeal_to
- Complaining → complain_to, criticize
- Apologizing → apologize_to
- Informing → inform, tell_something_to

ALLOWED BEHAVIORS: [{behaviorListFormatted}]

Respond with ONLY the behavior term, nothing else. Example: advise";

        var events = new List<DetectedEvent>();

        for (int idx = 0; idx < orderedMessages.Count; idx++)
        {
            var msg = orderedMessages[idx];
            ct.ThrowIfCancellationRequested();

            // Build context: 1 message before, current message, 1 message after
            var contextLines = new List<string>();
            if (idx > 0)
                contextLines.Add($"[Previous] {orderedMessages[idx - 1].Speaker}: {orderedMessages[idx - 1].Content}");
            contextLines.Add($"[THIS MESSAGE] {msg.Speaker}: {msg.Content}");
            if (idx < orderedMessages.Count - 1)
                contextLines.Add($"[Next] {orderedMessages[idx + 1].Speaker}: {orderedMessages[idx + 1].Content}");

            var userPrompt = $"What behavior best describes THIS MESSAGE?\n\n{string.Join("\n", contextLines)}";

            string behavior = "acknowledge"; // fallback
            const int MaxRetries = 2;

            _logger.LogInformation("Processing message {Order}/{Total}: {Speaker}: {Preview}",
                msg.Order + 1, orderedMessages.Count, msg.Speaker,
                msg.Content.Length > 80 ? msg.Content.Substring(0, 80) + "..." : msg.Content);

            for (int attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    var chatMessages = new List<ChatMessage>
                    {
                        new ChatMessage(ChatRole.System, systemPrompt),
                        new ChatMessage(ChatRole.User, userPrompt)
                    };

                    // Per-message timeout: 60 seconds max
                    using var msgCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    msgCts.CancelAfter(TimeSpan.FromSeconds(60));

                    var response = await _chatClient.GetResponseAsync(chatMessages, cancellationToken: msgCts.Token);
                    var responseText = (response.Text ?? "").Trim();

                    // LLM should return just one behavior term — clean it up
                    // Remove quotes, brackets, JSON wrapping if any
                    responseText = responseText.Trim('"', '\'', '[', ']', '{', '}', ' ', '\n', '\r');

                    // If the response contains a colon (like "behavior: greet"), take the part after
                    if (responseText.Contains(':'))
                        responseText = responseText.Split(':').Last().Trim().Trim('"', '\'');

                    // If multi-word with explanation, take just the first line/word that matches
                    if (responseText.Contains('\n'))
                        responseText = responseText.Split('\n')[0].Trim();

                    if (validBehaviors.Contains(responseText))
                    {
                        behavior = responseText;
                        break;
                    }

                    // Try fuzzy match
                    var corrected = TryFuzzyMatchBehavior(responseText, availableBehaviors);
                    if (corrected != null)
                    {
                        _logger.LogInformation("Msg {Order}: corrected '{Raw}' → '{Corrected}'", msg.Order, responseText, corrected);
                        behavior = corrected;
                        break;
                    }

                    // Force match on last attempt
                    if (attempt == MaxRetries - 1)
                    {
                        behavior = ForceMatchBehavior(responseText, availableBehaviors);
                        _logger.LogWarning("Msg {Order}: force-matched '{Raw}' → '{Forced}'", msg.Order, responseText, behavior);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Msg {Order}: LLM timed out after 60s (attempt {Attempt}). Using fallback.", msg.Order, attempt + 1);
                    break; // Don't retry on timeout — use fallback
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Behavior assignment failed for message {Order} (attempt {Attempt}).", msg.Order, attempt + 1);
                    if (attempt == MaxRetries - 1)
                        _logger.LogError("Giving up on message {Order}, using fallback '{Behavior}'.", msg.Order, behavior);
                }
            }

            // Object = the other speaker
            var objectSpeaker = uniqueSpeakers.FirstOrDefault(s =>
                !s.Equals(msg.Speaker, StringComparison.OrdinalIgnoreCase)) ?? uniqueSpeakers.Last();

            events.Add(new DetectedEvent
            {
                ActorSpeaker = msg.Speaker,
                Behavior = behavior,
                ObjectSpeaker = objectSpeaker,
                OriginalMessage = msg.Content
            });

            _logger.LogInformation("Msg {Order} ({Speaker}): {Behavior}", msg.Order, msg.Speaker, behavior);
        }

        // Per INTERACT: one situation for fixed identities
        var identityDesc = string.Join(" and ", speakers.Select(s => $"{s.DisplayName} ({s.Identity})"));
        var situation = new DetectedSituation
        {
            Name = "Interaction",
            Description = $"Conversation between {identityDesc}",
            Events = events
        };

        _logger.LogInformation("Built interaction with {EventCount} events from {MsgCount} messages.",
            events.Count, messages.Count);

        return new List<DetectedSituation> { situation };
    }

    /// <summary>
    /// Extract JSON from a response that might contain markdown code blocks or extra text.
    /// Uses bracket matching to find the correct closing bracket, handling cases where
    /// the LLM outputs multiple arrays or trailing content.
    /// </summary>
    private string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        // Handle markdown code blocks
        if (trimmed.Contains("```json"))
        {
            var start = trimmed.IndexOf("```json") + 7;
            var end = trimmed.IndexOf("```", start);
            if (end > start)
            {
                trimmed = trimmed.Substring(start, end - start).Trim();
            }
        }
        else if (trimmed.Contains("```"))
        {
            var start = trimmed.IndexOf("```") + 3;
            var end = trimmed.IndexOf("```", start);
            if (end > start)
            {
                trimmed = trimmed.Substring(start, end - start).Trim();
            }
        }

        // Find the first '[' and its matching ']' using bracket counting
        var jsonStart = trimmed.IndexOf('[');
        if (jsonStart < 0)
        {
            _logger.LogWarning("ExtractJson: No JSON array found in LLM response. First 100 chars: {Preview}",
                trimmed.Length > 100 ? trimmed.Substring(0, 100) + "..." : trimmed);
            return "[]";
        }

        int depth = 0;
        bool inString = false;
        int jsonEnd = -1;
        for (int i = jsonStart; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c == '"' && (i == 0 || trimmed[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString)
            {
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) { jsonEnd = i; break; }
                }
            }
        }

        if (jsonEnd > jsonStart)
        {
            return trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        // Fallback: use LastIndexOf if bracket matching failed (unbalanced quotes etc.)
        jsonEnd = trimmed.LastIndexOf(']');
        if (jsonEnd > jsonStart)
        {
            return trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        _logger.LogWarning("ExtractJson: No valid JSON array found in LLM response. First 100 chars: {Preview}",
            trimmed.Length > 100 ? trimmed.Substring(0, 100) + "..." : trimmed);
        return "[]";
    }

    /// <summary>
    /// Try to parse structured chat formats deterministically without LLM.
    /// Handles formats like "Speaker: message" with clear labels at line starts.
    /// Returns null if the format is not clearly structured.
    /// </summary>
    // Common words that appear with colons in text but are NOT speaker labels.
    // German + English words that frequently start lines in structured content.
    private static readonly HashSet<string> _denyListSpeakers = new(StringComparer.OrdinalIgnoreCase)
    {
        // German
        "Pause", "Beispiel", "Beispiele", "Hinweis", "Tipp", "Tipps", "Antwort", "Frage",
        "Ergebnis", "Zusammenfassung", "Übung", "Aufgabe", "Schritt", "Ziel", "Methode",
        "Vorteil", "Nachteil", "Wichtig", "Achtung", "Alternative", "Vorschlag",
        "Hier einige Tipps", "Hier einige", "Zum Beispiel", "Das heißt",
        // English
        "Example", "Note", "Tip", "Tips", "Answer", "Question", "Result", "Summary",
        "Exercise", "Task", "Step", "Goal", "Method", "Important", "Warning",
        "Hint", "Option", "Source", "Reference", "Output", "Input", "Response",
        // Numbered/structural
        "Lernblock", "Block", "Phase", "Teil", "Abschnitt", "Punkt",
    };

    private List<ParsedMessage>? TryDeterministicParse(string rawInput)
    {
        // Match lines that start with a short speaker label (max 20 chars) followed by a colon
        // e.g., "User: hello", "Bot: hi there", "A: message", "Person 1: text"
        var speakerPattern = new Regex(@"^([A-Za-zÀ-ÿ0-9_ ]{1,20}):\s", RegexOptions.Multiline);
        var matches = speakerPattern.Matches(rawInput);

        if (matches.Count < 2) return null;

        // Count how often each label appears — real speakers appear multiple times.
        // Filter out known non-speaker words from the deny list.
        var labelCounts = matches
            .Select(m => m.Groups[1].Value.Trim())
            .Where(label => !_denyListSpeakers.Contains(label))
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Real speaker labels: appear at least twice AND are max 2 words.
        // This filters out content lines like "Hier einige Tipps:" or "Beispiele für relevante Studien:"
        // Real chat labels: "User", "Bot", "Person 1", "Speaker A", etc.
        var validSpeakers = new HashSet<string>(
            labelCounts
                .Where(kvp => kvp.Value >= 2 && kvp.Key.Split(' ').Length <= 2)
                .Select(kvp => kvp.Key),
            StringComparer.OrdinalIgnoreCase);

        if (validSpeakers.Count < 2) return null;

        // Filter matches to only valid (recurring, short) speakers
        var validMatches = matches.Where(m => validSpeakers.Contains(m.Groups[1].Value.Trim())).ToList();

        // Build messages by splitting at valid speaker labels
        var result = new List<ParsedMessage>();
        for (int i = 0; i < validMatches.Count; i++)
        {
            var speaker = validMatches[i].Groups[1].Value.Trim();
            var contentStart = validMatches[i].Index + validMatches[i].Length;
            var contentEnd = (i + 1 < validMatches.Count) ? validMatches[i + 1].Index : rawInput.Length;
            var content = rawInput.Substring(contentStart, contentEnd - contentStart).Trim();

            if (!string.IsNullOrWhiteSpace(content))
            {
                result.Add(new ParsedMessage
                {
                    Speaker = speaker,
                    Content = content,
                    Order = result.Count
                });
            }
        }

        return result.Count >= 2 ? result : null;
    }

    /// <summary>
    /// Try to fuzzy-match an invalid behavior to a valid one.
    /// Uses underscore/space normalization, prefix matching, word matching,
    /// and Levenshtein distance as a final fallback.
    /// When multiple matches exist, picks the shortest (most generic) behavior.
    /// </summary>
    private string? TryFuzzyMatchBehavior(string invalidBehavior, List<string> validBehaviors)
    {
        var lower = invalidBehavior.Trim().ToLowerInvariant();

        // 1. Try exact match with underscores replaced by spaces (or vice versa)
        var withSpaces = lower.Replace("_", " ");
        var withUnderscores = lower.Replace(" ", "_");
        var exactAlt = validBehaviors.FirstOrDefault(b =>
            b.Equals(withSpaces, StringComparison.OrdinalIgnoreCase) ||
            b.Equals(withUnderscores, StringComparison.OrdinalIgnoreCase));
        if (exactAlt != null) return exactAlt;

        // 2. Try: valid behavior starts with the invalid term — pick shortest if multiple
        var startsWithMatches = validBehaviors
            .Where(b => b.StartsWith(lower, StringComparison.OrdinalIgnoreCase) ||
                        b.StartsWith(lower + " ", StringComparison.OrdinalIgnoreCase) ||
                        b.StartsWith(lower + "_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (startsWithMatches.Count == 1) return startsWithMatches[0];
        if (startsWithMatches.Count > 1) return startsWithMatches.OrderBy(b => b.Length).First();

        // 3. Try: valid behavior contains the invalid term as a whole word — pick shortest if multiple
        var containsMatches = validBehaviors
            .Where(b => b.Split(' ', '_').Any(word => word.Equals(lower, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (containsMatches.Count == 1) return containsMatches[0];
        if (containsMatches.Count > 1) return containsMatches.OrderBy(b => b.Length).First();

        // 4. Levenshtein distance fallback (proportional threshold, only for terms ≥ 5 chars)
        if (lower.Length >= 5)
        {
            // Allow ~20% edit distance, minimum 2, maximum 3
            int maxDistance = Math.Clamp(lower.Length / 5, 2, 3);
            var closest = validBehaviors
                .Select(b => (behavior: b, distance: LevenshteinDistance(lower, b.ToLowerInvariant())))
                .Where(x => x.distance <= maxDistance)
                .OrderBy(x => x.distance)
                .ThenBy(x => x.behavior.Length)
                .FirstOrDefault();
            if (closest.behavior != null) return closest.behavior;
        }

        return null;
    }

    /// <summary>
    /// Force-match an invalid behavior to the closest valid one using Levenshtein distance
    /// with no threshold. This is the last resort — every event gets a behavior.
    /// </summary>
    private string ForceMatchBehavior(string invalidBehavior, List<string> validBehaviors)
    {
        // First try the normal fuzzy match (which has thresholds)
        var fuzzy = TryFuzzyMatchBehavior(invalidBehavior, validBehaviors);
        if (fuzzy != null) return fuzzy;

        // Force: pick the closest behavior by Levenshtein distance, no threshold
        var lower = invalidBehavior.Trim().ToLowerInvariant();
        var closest = validBehaviors
            .Select(b => (behavior: b, distance: LevenshteinDistance(lower, b.ToLowerInvariant())))
            .OrderBy(x => x.distance)
            .ThenBy(x => x.behavior.Length)
            .First();
        return closest.behavior;
    }

    /// <summary>
    /// Use the LLM to semantically map invalid behaviors to valid ones.
    /// Returns a dictionary of lowercase-invalid → valid behavior.
    /// </summary>
    private async Task<Dictionary<string, string>> TryLlmCorrectBehaviorsAsync(
        List<string> invalidBehaviors,
        List<string> validBehaviors,
        CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var invalidList = string.Join(", ", invalidBehaviors.Select(b => $"\"{b}\""));
            var validList = string.Join(", ", validBehaviors.Select(b => $"\"{b}\""));

            var prompt = $@"Map each invalid ACT behavior to the single closest semantic match from the allowed list.

Invalid behaviors: [{invalidList}]

Allowed behaviors: [{validList}]

Return ONLY a JSON object mapping each invalid behavior to a valid one. Example:
{{""reassure"": ""comfort"", ""confirm"": ""agree_with""}}";

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, "You map behavioral terms to their closest semantic equivalents. Return only valid JSON."),
                new ChatMessage(ChatRole.User, prompt)
            };

            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var jsonText = SanitizeLlmJson(response.Text ?? "{}");

            // Extract JSON object
            var objStart = jsonText.IndexOf('{');
            var objEnd = jsonText.LastIndexOf('}');
            if (objStart >= 0 && objEnd > objStart)
            {
                jsonText = jsonText.Substring(objStart, objEnd - objStart + 1);
            }

            var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonText, LlmJsonOptions);
            if (mapping != null)
            {
                foreach (var kvp in mapping)
                {
                    result[kvp.Key.ToLowerInvariant()] = kvp.Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM behavior correction call failed — skipping.");
        }
        return result;
    }

    /// <summary>
    /// Sanitize common LLM JSON quirks before deserialization.
    /// Handles: unescaped control chars in strings, missing commas between objects/arrays.
    /// Trailing commas are handled by <see cref="LlmJsonOptions"/> (AllowTrailingCommas).
    /// </summary>
    private static string SanitizeLlmJson(string json)
    {
        // 1. Escape literal control characters (newline, tab, etc.) inside JSON string values.
        //    Ollama/local models often output raw newlines inside strings instead of \n.
        json = EscapeControlCharsInStrings(json);

        // 2. Remove ellipsis and trailing dots that LLMs add as comments inside JSON
        //    e.g. "events": [ ... ] or stray "..." between properties
        json = Regex.Replace(json, @",\s*\.\.\.+\s*([}\]])", "$1");  // , ... } or , ... ]
        json = Regex.Replace(json, @"\.\.\.+", "");                   // remaining ... anywhere

        // 3. Insert missing commas between adjacent objects/arrays: }{ → },{ etc.
        json = Regex.Replace(json, @"}\s*{", "},{");
        json = Regex.Replace(json, @"}\s*\[", "},[");
        json = Regex.Replace(json, @"]\s*{", "],{");
        // Insert missing comma after a closing quote followed by a new key: "value"  "key" → "value", "key"
        json = Regex.Replace(json, @"""\s+""(?=[^:]*"":)", @""", """);
        return json;
    }

    /// <summary>
    /// Escape raw control characters (newline, carriage return, tab) that appear
    /// inside JSON string values. Uses a simple state machine to track string boundaries.
    /// </summary>
    private static string EscapeControlCharsInStrings(string json)
    {
        var sb = new StringBuilder(json.Length);
        bool inString = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i - 1] != '\\'))
            {
                inString = !inString;
                sb.Append(c);
            }
            else if (inString && c == '\n')
            {
                sb.Append("\\n");
            }
            else if (inString && c == '\r')
            {
                // skip \r (will be covered by \n in \r\n sequences)
            }
            else if (inString && c == '\t')
            {
                sb.Append("\\t");
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Compute the Levenshtein edit distance between two strings.
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
