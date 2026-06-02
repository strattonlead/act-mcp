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

    /// <summary>
    /// Optional decoding temperature override. When null, the provider default applies
    /// (Ollama default is 0.8). Set via the CHAT_TEMPERATURE environment variable; used for
    /// the robustness temperature sweep.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// Behavior-selection prompt variant: "P0" (default, original study prompt) or "P1"
    /// (alternative used for the robustness analysis). Set via the PROMPT_VARIANT env var.
    /// </summary>
    public string PromptVariant { get; set; } = "P0";
}
