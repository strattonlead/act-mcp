using ACT.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
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

    public Task<InteractionResult> CalculateInteractionAsync(Interaction interaction)
    {
        return CalculateInteractionAsync(interaction, transientsByIdentity: null);
    }

    public async Task<InteractionResult> CalculateInteractionAsync(Interaction interaction, Dictionary<string, double[]>? transientsByIdentity)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts/calculate_interaction.R");
        if (!File.Exists(scriptPath))
        {
             var devPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../ACT/Scripts/calculate_interaction.R"));
             if (File.Exists(devPath)) scriptPath = devPath;
        }

        // Determine gender for EPA lookup: use actor's gender setting, default to "average"
        var gender = !string.IsNullOrEmpty(interaction.Actor.Gender) ? interaction.Actor.Gender : "average";

        var argsList = new List<string>
        {
            interaction.Actor.Identity,
            interaction.Behavior,
            interaction.Object.Identity,
            gender
        };

        // When chaining events within a situation, look up previous transient EPAs
        // by identity name. This correctly handles actor/object role swaps between events.
        // E.g., if E1 was student→assistant and E2 is assistant→student,
        // E2's actor input (assistant) uses E1's *object* transient, not E1's actor transient.
        if (transientsByIdentity != null)
        {
            var actorIdentity = interaction.Actor.Identity;
            var objectIdentity = interaction.Object.Identity;

            if (transientsByIdentity.TryGetValue(actorIdentity, out var actorTrans) &&
                transientsByIdentity.TryGetValue(objectIdentity, out var objectTrans))
            {
                argsList.Add(actorTrans[0].ToString(CultureInfo.InvariantCulture));
                argsList.Add(actorTrans[1].ToString(CultureInfo.InvariantCulture));
                argsList.Add(actorTrans[2].ToString(CultureInfo.InvariantCulture));
                argsList.Add(objectTrans[0].ToString(CultureInfo.InvariantCulture));
                argsList.Add(objectTrans[1].ToString(CultureInfo.InvariantCulture));
                argsList.Add(objectTrans[2].ToString(CultureInfo.InvariantCulture));
            }
            // If an identity is not found in the dictionary (e.g., new participant),
            // fall back to fundamentals by not passing extra args.
        }

        var args = argsList.ToArray();

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
