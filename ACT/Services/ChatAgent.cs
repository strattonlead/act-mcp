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

    public async Task<List<ParsedMessage>> ParseMessagesAsync(string rawInput, CancellationToken ct = default)
    {
        var systemPrompt = @"You are an expert at parsing conversation transcripts. 
Your task is to identify individual messages from a raw conversation and determine who the speaker is.

Rules:
- Look for patterns like 'User:', 'Bot:', 'A:', 'B:', or similar speaker indicators
- If no explicit labels exist, infer speakers from context (alternating pattern, etc.)
- Preserve the order of messages
- Handle various formats: chat logs, transcripts, dialogue scripts

Return a JSON array with this exact format (no other text):
[
  {""speaker"": ""<speaker_label>"", ""content"": ""<message_text>"", ""order"": <0-indexed_position>}
]

Example input:
User: Hello
Bot: Hi there!

Example output:
[{""speaker"": ""User"", ""content"": ""Hello"", ""order"": 0}, {""speaker"": ""Bot"", ""content"": ""Hi there!"", ""order"": 1}]";

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, rawInput)
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var jsonText = SanitizeLlmJson(ExtractJson(response.Text ?? "[]"));

            var parsed = JsonSerializer.Deserialize<List<ParsedMessage>>(jsonText, LlmJsonOptions);
            
            return parsed ?? new List<ParsedMessage>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse messages from LLM.");
            throw;
        }
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

    public async Task<List<DetectedSituation>> DetectSituationsAsync(
        List<ParsedMessage> messages, 
        List<LabeledSpeaker> speakers,
        List<string> availableBehaviors, 
        CancellationToken ct = default)
    {
        // Build speaker mapping for context
        var speakerMap = speakers.ToDictionary(s => s.OriginalLabel, s => s.Identity);
        
        // Validate set
        var validBehaviors = new HashSet<string>(availableBehaviors, StringComparer.OrdinalIgnoreCase);
        
        var behaviorListFormatted = string.Join("\n", availableBehaviors.Select(b => $"  - {b}"));

        var systemPrompt = $@"You are an expert in Affect Control Theory (ACT).
Your task is to analyze a conversation and:
1. Divide it into logical situations (context shifts, topic changes)
2. For each situation, extract ACT events (Actor performs Behavior towards Object)

Speaker identity mapping:
{string.Join("\n", speakers.Select(s => $"- {s.OriginalLabel} = {s.Identity}"))}

ALLOWED BEHAVIORS (you MUST use one of these EXACTLY as written — no synonyms, no abbreviations, no invented terms):
{behaviorListFormatted}

CRITICAL Rules:
1. You MUST choose a behavior EXACTLY from the allowed list above. Copy the term character-for-character.
2. Many behaviors are multi-word phrases (e.g. ""offer something to"", ""agree with"", ""ask about""). Use the FULL phrase, not a shortened form.
3. Do NOT invent new behaviors or use synonyms. If unsure, pick the closest match from the list above.

Return a JSON array with this exact format (no other text):
[
  {{
    ""name"": ""Situation Name"",
    ""description"": ""Brief description of context"",
    ""events"": [
      {{
        ""actorSpeaker"": ""<original_speaker_label>"",
        ""behavior"": ""<act_behavior_term>"",
        ""objectSpeaker"": ""<original_speaker_label_of_recipient>"",
        ""originalMessage"": ""<the_message_text>""
      }}
    ]
  }}
]

Guidelines:
- Each message typically generates one event
- The speaker is the Actor, the other participant is the Object
- Choose behaviors that capture the emotional/social meaning of the message
- Group related exchanges into the same situation";

        var conversationText = string.Join("\n", messages.Select(m => $"{m.Speaker}: {m.Content}"));

        var chatMessages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, $"Analyze this conversation:\n\n{conversationText}")
        };

        const int MaxRetries = 3;
        for (int i = 0; i < MaxRetries; i++)
        {
            try
            {
                var response = await _chatClient.GetResponseAsync(chatMessages, cancellationToken: ct);
                var responseText = response.Text ?? "[]";
                var jsonText = SanitizeLlmJson(ExtractJson(responseText));

                var situations = JsonSerializer.Deserialize<List<DetectedSituation>>(jsonText, LlmJsonOptions) ?? new List<DetectedSituation>();
                
                // Fuzzy auto-correction: try to fix invalid behaviors before retrying
                foreach (var sit in situations)
                {
                    if (sit.Events == null) continue;
                    foreach (var evt in sit.Events)
                    {
                        if (!validBehaviors.Contains(evt.Behavior))
                        {
                            var corrected = TryFuzzyMatchBehavior(evt.Behavior, availableBehaviors);
                            if (corrected != null)
                            {
                                _logger.LogInformation("Auto-corrected behavior '{Invalid}' → '{Corrected}'", evt.Behavior, corrected);
                                evt.Behavior = corrected;
                            }
                        }
                    }
                }

                // Collect still-invalid behaviors for LLM correction
                var stillInvalid = situations
                    .Where(s => s.Events != null)
                    .SelectMany(s => s.Events!)
                    .Where(e => !validBehaviors.Contains(e.Behavior))
                    .Select(e => e.Behavior)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // LLM-based semantic correction for behaviors fuzzy matching couldn't fix
                if (stillInvalid.Count > 0)
                {
                    var llmCorrectionMap = await TryLlmCorrectBehaviorsAsync(stillInvalid, availableBehaviors, ct);
                    foreach (var sit in situations)
                    {
                        if (sit.Events == null) continue;
                        foreach (var evt in sit.Events)
                        {
                            if (!validBehaviors.Contains(evt.Behavior) &&
                                llmCorrectionMap.TryGetValue(evt.Behavior.ToLowerInvariant(), out var llmCorrected) &&
                                validBehaviors.Contains(llmCorrected))
                            {
                                _logger.LogInformation("LLM-corrected behavior '{Invalid}' → '{Corrected}'", evt.Behavior, llmCorrected);
                                evt.Behavior = llmCorrected;
                            }
                        }
                    }
                }

                // Validate behaviors after all corrections
                var invalidEntries = new List<string>();
                foreach (var sit in situations)
                {
                    if (sit.Events != null)
                    {
                        foreach (var evt in sit.Events)
                        {
                            if (!validBehaviors.Contains(evt.Behavior))
                            {
                                invalidEntries.Add($"Behavior '{evt.Behavior}' in situation '{sit.Name}' is NOT in the allowed list.");
                            }
                        }
                    }
                }

                if (invalidEntries.Count == 0)
                {
                    return situations;
                }

                // If this is the last retry, drop invalid events and return partial results
                if (i == MaxRetries - 1)
                {
                    _logger.LogWarning("Exhausted retries. Dropping {Count} invalid events and returning partial results. Invalid: {Invalid}",
                        invalidEntries.Count, string.Join(", ", invalidEntries));

                    foreach (var sit in situations)
                    {
                        sit.Events = sit.Events?.Where(e => validBehaviors.Contains(e.Behavior)).ToList();
                    }
                    // Remove empty situations
                    situations = situations.Where(s => s.Events != null && s.Events.Count > 0).ToList();
                    return situations;
                }

                // Otherwise prepare for retry with the behavior list re-included
                _logger.LogWarning("LLM returned invalid behaviors. Retrying ({RetryCount}/{MaxRetries}). Invalid: {Invalid}", i + 1, MaxRetries, string.Join(", ", invalidEntries));

                chatMessages.Add(new ChatMessage(ChatRole.Assistant, responseText));
                chatMessages.Add(new ChatMessage(ChatRole.User,
                    $"ERROR: The following behaviors are invalid: {string.Join("; ", invalidEntries)}.\n\n" +
                    $"Here is the complete list of allowed behaviors again. You MUST pick ONLY from this list:\n{behaviorListFormatted}\n\n" +
                    $"Please return the corrected full JSON again."));

            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Failed to detect situations from LLM (Attempt {Retry}).", i + 1);
                 if (i == MaxRetries - 1) throw;
            }
        }

        // Should not be reached, but return empty as safety net
        return new List<DetectedSituation>();
    }

    /// <summary>
    /// Extract JSON from a response that might contain markdown code blocks or extra text.
    /// </summary>
    private string ExtractJson(string text)
    {
        // Try to find JSON array in the text
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
        
        // Find the JSON array
        var jsonStart = trimmed.IndexOf('[');
        var jsonEnd = trimmed.LastIndexOf(']');
        
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            return trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }
        
        return trimmed;
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

        // 4. Levenshtein distance fallback (threshold ≤ 3 edits, only for terms ≥ 4 chars)
        if (lower.Length >= 4)
        {
            var closest = validBehaviors
                .Select(b => (behavior: b, distance: LevenshteinDistance(lower, b.ToLowerInvariant())))
                .Where(x => x.distance <= 3)
                .OrderBy(x => x.distance)
                .ThenBy(x => x.behavior.Length)
                .FirstOrDefault();
            if (closest.behavior != null) return closest.behavior;
        }

        return null;
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
    /// Sanitize common LLM JSON quirks (trailing commas, etc.) before deserialization.
    /// </summary>
    private static string SanitizeLlmJson(string json)
    {
        // Remove trailing commas before ] or }
        return Regex.Replace(json, @",\s*([}\]])", "$1");
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
