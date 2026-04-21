using ACT.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Services;

public interface IAnnotateService
{
    Task<AnnotateResponseDto> AnnotateAsync(string text, string runId, int phase, CancellationToken ct = default);
}

public class AnnotateService : IAnnotateService
{
    private readonly IActService _actService;
    private readonly IConversationService _conversationService;
    private readonly IChatAgent _chatAgent;
    private readonly ILogger<AnnotateService> _logger;

    // Study-domain roles. The germany2007 dictionary has both "student" and "assistant"
    // as identities (see Scripts/calculate_interaction.R defaults).
    private const string ActorIdentity = "student";
    private const string ObjectIdentity = "assistant";
    private const string ActorLabel = "Student";
    private const string ObjectLabel = "Assistant";
    private const string SituationType = "tutoring";

    public AnnotateService(
        IActService actService,
        IConversationService conversationService,
        IChatAgent chatAgent,
        ILogger<AnnotateService> logger)
    {
        _actService = actService;
        _conversationService = conversationService;
        _chatAgent = chatAgent;
        _logger = logger;
    }

    public async Task<AnnotateResponseDto> AnnotateAsync(string text, string runId, int phase, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("text must not be empty", nameof(text));
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("runId must not be empty", nameof(runId));

        var dictKey = await ResolveDictionaryKeyAsync();
        var conversation = await GetOrCreateConversationAsync(runId, dictKey);
        var situation = conversation.Situations.FirstOrDefault()
                        ?? await _conversationService.AddSituationAsync(conversation.Id, SituationType);

        var behaviors = await _actService.GetDictionaryBehaviorsAsync(dictKey, ct);
        if (behaviors.Count == 0)
        {
            throw new InvalidOperationException($"No behaviors available in dictionary '{dictKey}'");
        }

        var behavior = await _chatAgent.ExtractSingleBehaviorAsync(
            text, behaviors, ActorIdentity, ObjectIdentity, ct);

        var interaction = new Interaction
        {
            Actor = new Person { Name = ActorLabel, Identity = ActorIdentity },
            Object = new Person { Name = ObjectLabel, Identity = ObjectIdentity },
            Behavior = behavior,
            OriginalMessage = text
        };

        await _conversationService.AddEventAsync(conversation.Id, situation, interaction);

        if (interaction.Result == null)
        {
            throw new InvalidOperationException("ACT calculation did not return a result");
        }

        var preferredEpa = await _actService.GetIdentityEpaAsync(ObjectIdentity, dictKey, ct: ct);

        return new AnnotateResponseDto
        {
            Epa = EpaDto.From(interaction.Result.TransientActorEPA),
            Deflection = interaction.Result.Deflection,
            SituationContext = PhaseToSituationContext(phase),
            PreferredResponseEPA = EpaDto.From(preferredEpa)
        };
    }

    private async Task<Conversation> GetOrCreateConversationAsync(string runId, string dictKey)
    {
        var existing = await _conversationService.GetLatestBySessionIdAsync(runId);
        if (existing != null)
        {
            if (existing.DictionaryKey != dictKey)
            {
                existing.DictionaryKey = dictKey;
                await _conversationService.UpdateAsync(existing);
            }
            return existing;
        }

        var created = await _conversationService.CreateAsync($"Studyflow run {runId}", dictKey);
        created.SessionId = runId;
        await _conversationService.UpdateAsync(created);
        return created;
    }

    private async Task<string> ResolveDictionaryKeyAsync()
    {
        var envKey = Environment.GetEnvironmentVariable("DEFAULT_DICTIONARY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            return envKey;
        }

        var dictionaries = await _actService.GetDictionariesAsync();
        var germany = dictionaries.FirstOrDefault(d => d.Key.Equals("germany2007", StringComparison.OrdinalIgnoreCase));
        if (germany != null) return germany.Key;

        var first = dictionaries.FirstOrDefault();
        if (first != null) return first.Key;

        // Safe default matching existing scripts
        return "germany2007";
    }

    private static string PhaseToSituationContext(int phase) => phase switch
    {
        1 => "Situationserhebung",
        2 => "Vertiefung",
        3 => "Reflexion",
        _ => "Interaction"
    };
}

public class AnnotateResponseDto
{
    [JsonPropertyName("epa")]
    public EpaDto Epa { get; set; } = new();

    [JsonPropertyName("deflection")]
    public double Deflection { get; set; }

    [JsonPropertyName("situationContext")]
    public string SituationContext { get; set; } = string.Empty;

    [JsonPropertyName("preferredResponseEPA")]
    public EpaDto PreferredResponseEPA { get; set; } = new();
}

public class EpaDto
{
    [JsonPropertyName("e")]
    public double E { get; set; }

    [JsonPropertyName("p")]
    public double P { get; set; }

    [JsonPropertyName("a")]
    public double A { get; set; }

    public static EpaDto From(double[]? arr)
    {
        if (arr == null || arr.Length < 3)
            return new EpaDto();
        return new EpaDto { E = arr[0], P = arr[1], A = arr[2] };
    }
}
