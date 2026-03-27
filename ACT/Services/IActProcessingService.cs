using ACT.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ACT.Services;

public interface IActProcessingService
{
    Task<InteractionResult> CalculateInteractionAsync(Interaction interaction);

    /// <summary>
    /// Calculate interaction with transient chaining. The transientsByIdentity dictionary
    /// maps identity names to their transient EPAs from previous events.
    /// When actor/object roles swap between events, the correct transients are looked up
    /// by identity name rather than by positional actor/object slot.
    /// </summary>
    Task<InteractionResult> CalculateInteractionAsync(Interaction interaction, Dictionary<string, double[]>? transientsByIdentity);
}
