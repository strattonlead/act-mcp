namespace ACT.Services;

/// <summary>
/// Configuration for the LLM provider (local vs cloud).
/// Used by services to adjust concurrency and retry behavior.
/// </summary>
public class LlmProviderConfig
{
    /// <summary>
    /// True when using a local model (Ollama). Enables sequential processing.
    /// </summary>
    public bool IsLocalModel { get; set; }
}
