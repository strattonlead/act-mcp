using ACT.Models;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;

namespace ACT.Services;

public class ActProcessingService : IActProcessingService
{
    private readonly IRScriptRunner _rRunner;
    private readonly ILogger<ActProcessingService> _logger;

    public ActProcessingService(IRScriptRunner rRunner, ILogger<ActProcessingService> logger)
    {
        _rRunner = rRunner;
        _logger = logger;
    }

    public async Task<InteractionResult> CalculateInteractionAsync(Interaction interaction)
    {
        // Path to script
        // Assuming typical layout
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts/calculate_interaction.R");
        if (!File.Exists(scriptPath)) 
        {
             // Try searching relative to project root for dev time
             // This is a bit hacky but works for now
             var devPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../ACT/Scripts/calculate_interaction.R"));
             if (File.Exists(devPath)) scriptPath = devPath;
        }

        var args = new[] 
        { 
            interaction.Actor.Identity, 
            interaction.Behavior, 
            interaction.Object.Identity 
        };

        try
        {
            var procResult = await _rRunner.RunAsync(scriptPath, args);
            if (!procResult.IsSuccess)
            {
                _logger.LogError("R script failed: {StdErr}", procResult.StdErr);
                throw new Exception($"R script execution failed: {procResult.StdErr}");
            }

            var json = procResult.StdOut;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.GetProperty("success").GetBoolean())
            {
                 var err = root.GetProperty("error").GetString();
                 throw new Exception($"ACT Calculation Error: {err}");
            }

            var res = new InteractionResult();
            res.Deflection = root.GetProperty("deflection").GetDouble();
            
            // Helpers to read array
            double[] ReadArray(JsonElement el) 
            {
                return new double[] { el[0].GetDouble(), el[1].GetDouble(), el[2].GetDouble() };
            }

            res.TransientActorEPA = ReadArray(root.GetProperty("transient_actor"));
            res.TransientBehaviorEPA = ReadArray(root.GetProperty("transient_behavior"));
            res.TransientObjectEPA = ReadArray(root.GetProperty("transient_object"));
            
            if (root.TryGetProperty("actor_emotion", out var ae)) res.ActorEmotionEPA = ReadArray(ae);
            if (root.TryGetProperty("object_emotion", out var oe)) res.ObjectEmotionEPA = ReadArray(oe);

            return res;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating interaction");
            throw;
        }
    }
}
