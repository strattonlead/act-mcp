using ACT.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Services;

public interface IChatAgent
{
    /// <summary>
    /// Extract ACT events from a conversation history.
    /// </summary>
    Task<string> ExtractActEventsAsync(string conversationHistory, List<string>? availableBehaviors = null, CancellationToken ct = default);
    
    /// <summary>
    /// Parse raw conversation text into individual messages with speaker identification.
    /// </summary>
    Task<List<ParsedMessage>> ParseMessagesAsync(string rawInput, CancellationToken ct = default);
    
    /// <summary>
    /// Label speakers with ACT identities from the dictionary.
    /// </summary>
    Task<List<LabeledSpeaker>> LabelIdentitiesAsync(
        List<ParsedMessage> messages, 
        List<string> availableIdentities, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Detect situations and extract events from the conversation.
    /// </summary>
    Task<List<DetectedSituation>> DetectSituationsAsync(
        List<ParsedMessage> messages, 
        List<LabeledSpeaker> speakers,
        List<string> availableBehaviors, 
        CancellationToken ct = default);
}
