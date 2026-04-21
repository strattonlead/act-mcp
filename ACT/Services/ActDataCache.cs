using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Services;

public interface IActDataCache
{
    Task<List<ActDictionaryDto>> GetDictionariesAsync(System.Func<Task<List<ActDictionaryDto>>> factory);
    Task<List<string>> GetIdentitiesAsync(string key, System.Func<Task<List<string>>> factory);
    Task<List<string>> GetBehaviorsAsync(string key, System.Func<Task<List<string>>> factory);
    Task<List<ActSuggestionDto>> GetSuggestionsAsync(string key, System.Func<Task<List<ActSuggestionDto>>> factory);
    Task<double[]> GetIdentityEpaAsync(string key, System.Func<Task<double[]>> factory);
}

public class ActDataCache : IActDataCache
{
    // Cache for the list of dictionaries
    private List<ActDictionaryDto> _dictionaries;
    private readonly SemaphoreSlim _dictionariesLock = new(1, 1);

    // Cache for identities per dictionary key
    private readonly ConcurrentDictionary<string, List<string>> _identities = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _identityLocks = new();

    // Cache for behaviors per dictionary key
    private readonly ConcurrentDictionary<string, List<string>> _behaviors = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _behaviorLocks = new();

    // Cache for suggestions
    private readonly ConcurrentDictionary<string, List<ActSuggestionDto>> _suggestions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _suggestionLocks = new();

    // Cache for identity EPAs, keyed as "<dictKey>:<identity>:<gender>"
    private readonly ConcurrentDictionary<string, double[]> _identityEpas = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _identityEpaLocks = new();

    public async Task<List<ActDictionaryDto>> GetDictionariesAsync(System.Func<Task<List<ActDictionaryDto>>> factory)
    {
        if (_dictionaries != null)
        {
            return _dictionaries;
        }

        await _dictionariesLock.WaitAsync();
        try
        {
            if (_dictionaries != null)
            {
                return _dictionaries;
            }

            _dictionaries = await factory();
            return _dictionaries;
        }
        finally
        {
            _dictionariesLock.Release();
        }
    }

    public async Task<List<string>> GetIdentitiesAsync(string key, System.Func<Task<List<string>>> factory)
    {
        if (_identities.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Get or add a lock for this specific key to avoid stamped calls for the same dictionary
        var keyLock = _identityLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await keyLock.WaitAsync();
        try
        {
            if (_identities.TryGetValue(key, out cached))
            {
                return cached;
            }

            var result = await factory();
            _identities[key] = result;
            return result;
        }
        finally
        {
            keyLock.Release();
            // Optional: TryToRemove lock if we want to clean up, but keeping it is cheaper/safer for now
        }
    }

    public async Task<List<string>> GetBehaviorsAsync(string key, System.Func<Task<List<string>>> factory)
    {
        if (_behaviors.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var keyLock = _behaviorLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await keyLock.WaitAsync();
        try
        {
            if (_behaviors.TryGetValue(key, out cached))
            {
                return cached;
            }

            var result = await factory();
            _behaviors[key] = result;
            return result;
        }
        finally
        {
            keyLock.Release();
        }
    }
    public async Task<double[]> GetIdentityEpaAsync(string key, System.Func<Task<double[]>> factory)
    {
        if (_identityEpas.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var keyLock = _identityEpaLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await keyLock.WaitAsync();
        try
        {
            if (_identityEpas.TryGetValue(key, out cached))
            {
                return cached;
            }

            var result = await factory();
            _identityEpas[key] = result;
            return result;
        }
        finally
        {
            keyLock.Release();
        }
    }

    public async Task<List<ActSuggestionDto>> GetSuggestionsAsync(string key, System.Func<Task<List<ActSuggestionDto>>> factory)
    {
        if (_suggestions.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var keyLock = _suggestionLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await keyLock.WaitAsync();
        try
        {
            if (_suggestions.TryGetValue(key, out cached))
            {
                return cached;
            }

            var result = await factory();
            _suggestions[key] = result;
            return result;
        }
        finally
        {
            keyLock.Release();
        }
    }
}
