using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ACT.Services;

public interface IActService
{
    Task<List<ActDictionaryDto>> GetDictionariesAsync(CancellationToken ct = default);
    Task<List<string>> GetDictionaryIdentitiesAsync(string key, CancellationToken ct = default);
    Task<List<string>> GetDictionaryBehaviorsAsync(string key, CancellationToken ct = default);
}

public class ActService : IActService
{
    private readonly IRScriptRunner _rRunner;
    private readonly ILogger<ActService> _logger;

    public ActService(IRScriptRunner rRunner, ILogger<ActService> logger)
    {
        _rRunner = rRunner;
        _logger = logger;
    }

    public async Task<List<ActDictionaryDto>> GetDictionariesAsync(CancellationToken ct = default)
    {
        try
        {
            var scriptPath = "Scripts/get_act_dictionaries.R";
            // Check if we need to resolve it relative to base dir or if RScriptRunner handles it.
            // RScriptRunner checks AppContext.BaseDirectory combined with script path.
            // Since we copy "Scripts" folder to output, "Scripts/get_act_dictionaries.R" should work.
            
            var result = await _rRunner.RunJsonAsync<List<ActDictionaryDto>>(scriptPath, args: null, ct: ct);
            return result.Payload ?? new List<ActDictionaryDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ACT dictionaries.");
            return new List<ActDictionaryDto>();
        }
        }

        public async Task<List<string>> GetDictionaryIdentitiesAsync(string key, CancellationToken ct = default)
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
    }

    public async Task<List<string>> GetDictionaryBehaviorsAsync(string key, CancellationToken ct = default)
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
