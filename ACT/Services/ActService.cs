using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Services;

public interface IActService
{
    Task<List<ActDictionaryDto>> GetDictionariesAsync(CancellationToken ct = default);
    Task<List<string>> GetDictionaryIdentitiesAsync(string key, CancellationToken ct = default);
    Task<List<string>> GetDictionaryBehaviorsAsync(string key, CancellationToken ct = default);
    Task<List<ActSuggestionDto>> SuggestActionsAsync(string actor, string objectIdentity, string dictionaryKey = "germany2007", string gender = "male", CancellationToken ct = default);
    Task<double[]> GetIdentityEpaAsync(string identity, string dictionaryKey = "germany2007", string gender = "average", CancellationToken ct = default);
}

public class ActService : IActService
{
    private readonly IRScriptRunner _rRunner;
    private readonly IActDataCache _cache;
    private readonly ILogger<ActService> _logger;

    public ActService(IRScriptRunner rRunner, IActDataCache cache, ILogger<ActService> logger)
    {
        _rRunner = rRunner;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<ActDictionaryDto>> GetDictionariesAsync(CancellationToken ct = default)
    {
        return await _cache.GetDictionariesAsync(async () =>
        {
            try
            {
                var scriptPath = "Scripts/get_act_dictionaries.R";
                var result = await _rRunner.RunJsonAsync<List<ActDictionaryDto>>(scriptPath, args: null, ct: ct);
                return result.Payload ?? new List<ActDictionaryDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load ACT dictionaries.");
                return new List<ActDictionaryDto>();
            }
        });
    }

    public async Task<List<string>> GetDictionaryIdentitiesAsync(string key, CancellationToken ct = default)
    {
        return await _cache.GetIdentitiesAsync(key, async () =>
        {
            try
            {
                var scriptPath = "Scripts/get_act_identities.R";
                var result = await _rRunner.RunJsonAsync<List<string>>(scriptPath, args: new[] { key }, ct: ct);
                return result.Payload ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load identities for dictionary {Key}", key);
                return new List<string>();
            }
        });
    }

    public async Task<List<string>> GetDictionaryBehaviorsAsync(string key, CancellationToken ct = default)
    {
        return await _cache.GetBehaviorsAsync(key, async () =>
        {
            try
            {
                var scriptPath = "Scripts/get_act_behaviors.R";
                var result = await _rRunner.RunJsonAsync<List<string>>(scriptPath, args: new[] { key }, ct: ct);
                return result.Payload ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load behaviors for dictionary {Key}", key);
                return new List<string>();
            }
        });
    }
    public async Task<double[]> GetIdentityEpaAsync(string identity, string dictionaryKey = "germany2007", string gender = "average", CancellationToken ct = default)
    {
        var cacheKey = $"{dictionaryKey}:{identity}:{gender}";
        return await _cache.GetIdentityEpaAsync(cacheKey, async () =>
        {
            try
            {
                var scriptPath = "Scripts/get_identity_epa.R";
                var args = new[] { dictionaryKey, identity, gender };
                var result = await _rRunner.RunJsonAsync<IdentityEpaDto>(scriptPath, args, ct: ct);
                if (result.Payload != null && result.Payload.Error == null)
                {
                    return new[] { result.Payload.E, result.Payload.P, result.Payload.A };
                }
                _logger.LogWarning("get_identity_epa.R returned error for {Identity}/{Dict}: {Error}", identity, dictionaryKey, result.Payload?.Error);
                return new double[] { 0, 0, 0 };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load identity EPA for {Identity} in dictionary {Key}", identity, dictionaryKey);
                return new double[] { 0, 0, 0 };
            }
        });
    }

    public async Task<List<ActSuggestionDto>> SuggestActionsAsync(string actor, string objectIdentity, string dictionaryKey = "germany2007", string gender = "male", CancellationToken ct = default)
    {
        return await _cache.GetSuggestionsAsync($"{dictionaryKey}:{actor}:{objectIdentity}:{gender}", async () =>
        {
            try
            {
                var scriptPath = "Scripts/suggest_actions.R";
                var args = new[] { actor, objectIdentity, dictionaryKey, gender };
                var result = await _rRunner.RunJsonAsync<List<ActSuggestionDto>>(scriptPath, args, ct: ct);
                return result.Payload ?? new List<ActSuggestionDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to suggest actions for {Actor} -> {Object}", actor, objectIdentity);
                return new List<ActSuggestionDto>();
            }
        });
    }
}

public class ActDictionaryDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; }

    [JsonPropertyName("context")]
    public string Context { get; set; }

    [JsonPropertyName("year")]
    public string Year { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; }

    [JsonPropertyName("citation")]
    public string Citation { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; }

    [JsonPropertyName("individual")]
    public bool Individual { get; set; }

    [JsonPropertyName("components")]
    public JsonElement Components { get; set; }

    [JsonIgnore]
    public List<string> ComponentsList => JsonElementToStringList(Components);

    [JsonPropertyName("stats")]
    public JsonElement Stats { get; set; }

    [JsonIgnore]
    public List<string> StatsList => JsonElementToStringList(Stats);

    [JsonPropertyName("groups")]
    public JsonElement Groups { get; set; }

    [JsonIgnore]
    public List<string> GroupsList => JsonElementToStringList(Groups);



    private List<string> JsonElementToStringList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(e => e.ToString()).ToList();
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            return new List<string> { element.GetString() };
        }
        return new List<string>();
    }
}

public class ActSuggestionDto
{
    [JsonPropertyName("term")]
    public string Term { get; set; }

    [JsonPropertyName("deflection")]
    public double Deflection { get; set; }

    [JsonPropertyName("epa")]
    public List<double> Epa { get; set; }
}

public class IdentityEpaDto
{
    [JsonPropertyName("e")]
    public double E { get; set; }

    [JsonPropertyName("p")]
    public double P { get; set; }

    [JsonPropertyName("a")]
    public double A { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
