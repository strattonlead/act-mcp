using System.Collections.Generic;

namespace ACT.Models;

/// <summary>
/// Represents a parsed message from raw conversation input.
/// </summary>
public class ParsedMessage
{
    /// <summary>
    /// The speaker identifier (e.g., "User", "Bot").
    /// </summary>
    public string Speaker { get; set; } = string.Empty;
    
    /// <summary>
    /// The message content/text.
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Position in the conversation (0-indexed).
    /// </summary>
    public int Order { get; set; }
}

/// <summary>
/// Represents a speaker with their assigned ACT identity.
/// </summary>
public class LabeledSpeaker
{
    /// <summary>
    /// Original speaker label from conversation (e.g., "User", "Bot").
    /// </summary>
    public string OriginalLabel { get; set; } = string.Empty;
    
    /// <summary>
    /// Mapped ACT identity from dictionary (e.g., "student", "assistant").
    /// </summary>
    public string Identity { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name for the person.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Represents a detected situation with its events.
/// </summary>
public class DetectedSituation
{
    /// <summary>
    /// Name/type of the situation.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Description of the situation context.
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Events detected within this situation.
    /// </summary>
    public List<DetectedEvent> Events { get; set; } = new();
}

/// <summary>
/// Represents a detected ACT event before EPA calculation.
/// </summary>
public class DetectedEvent
{
    /// <summary>
    /// Speaker who performed the action.
    /// </summary>
    public string ActorSpeaker { get; set; } = string.Empty;
    
    /// <summary>
    /// ACT behavior term.
    /// </summary>
    public string Behavior { get; set; } = string.Empty;
    
    /// <summary>
    /// Speaker who received the action.
    /// </summary>
    public string ObjectSpeaker { get; set; } = string.Empty;
    
    /// <summary>
    /// Original message that generated this event.
    /// </summary>
    public string OriginalMessage { get; set; } = string.Empty;
}

/// <summary>
/// Holds the full state of an auto evaluation session.
/// </summary>
public class AutoEvaluationState
{
    /// <summary>
    /// The raw input text pasted by user.
    /// </summary>
    public string RawInput { get; set; } = string.Empty;
    
    /// <summary>
    /// Messages parsed from the raw input.
    /// </summary>
    public List<ParsedMessage> ParsedMessages { get; set; } = new();
    
    /// <summary>
    /// Selected dictionary key.
    /// </summary>
    public string DictionaryKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Speakers with their labeled identities.
    /// </summary>
    public List<LabeledSpeaker> LabeledSpeakers { get; set; } = new();
    
    /// <summary>
    /// Detected situations with events.
    /// </summary>
    public List<DetectedSituation> DetectedSituations { get; set; } = new();
    
    /// <summary>
    /// Final conversation with calculated results.
    /// </summary>
    public Conversation? FinalConversation { get; set; }
}
