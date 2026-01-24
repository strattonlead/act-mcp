using ACT.Models;
using System.Threading.Tasks;

namespace ACT.Services;

public interface IActProcessingService
{
    Task<InteractionResult> CalculateInteractionAsync(Interaction interaction);
}
