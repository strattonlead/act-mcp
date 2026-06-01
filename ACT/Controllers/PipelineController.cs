using ACT.Models;
using ACT.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ACT.Controllers;

/// <summary>
/// Headless endpoint that runs the EXACT paper pipeline (parse -> label -> dictionary-constrained
/// behavior detection -> chained deflection) on raw chat text and returns agent-JSON in the same
/// schema as the MongoDB conversation export consumed by chat-preprocessor/src/parse_agent.py.
///
/// It is decoupled from MongoDB / S3 / the Blazor UI so the robustness sweep (paper Section 5,
/// "Generalization and Robustness") can run many conditions unattended. Decoding temperature,
/// model, and prompt variant come from the server environment (CHAT_MODEL / CHAT_TEMPERATURE /
/// PROMPT_VARIANT); the sweep driver restarts the server once per condition.
///
/// The stage calls mirror BatchEvaluationService.ProcessFileAsync (the path that produced the
/// base data) exactly, including transient chaining, so results are directly comparable.
/// </summary>
[ApiController]
[Route("api/pipeline")]
public class PipelineController : ControllerBase
{
    private readonly IChatAgent _chatAgent;
    private readonly IActService _actService;
    private readonly IActProcessingService _processingService;
    private readonly ILogger<PipelineController> _logger;

    public PipelineController(
        IChatAgent chatAgent,
        IActService actService,
        IActProcessingService processingService,
        ILogger<PipelineController> logger)
    {
        _chatAgent = chatAgent;
        _actService = actService;
        _processingService = processingService;
        _logger = logger;
    }

    public class PipelineRequest
    {
        /// <summary>Conversation name, e.g. "01_chat" — parse_agent.py derives chat_id from this.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Raw chat transcript text (same content as the .txt uploaded in the batch UI).</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>ACT dictionary key; defaults to germany2007.</summary>
        public string? DictionaryKey { get; set; }
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] PipelineRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Text))
            return BadRequest("Text is required.");

        var dictKey = string.IsNullOrWhiteSpace(req.DictionaryKey) ? "germany2007" : req.DictionaryKey;

        var identities = await _actService.GetDictionaryIdentitiesAsync(dictKey);
        var behaviors = await _actService.GetDictionaryBehaviorsAsync(dictKey);

        // Stages 1-3: identical calls to BatchEvaluationService.ProcessFileAsync.
        var parsedMessages = await _chatAgent.ParseMessagesAsync(req.Text, ct);
        var labeledSpeakers = await _chatAgent.LabelIdentitiesAsync(parsedMessages, identities, ct);

        var speakerMap = new Dictionary<string, LabeledSpeaker>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in labeledSpeakers)
        {
            speakerMap.TryAdd(s.OriginalLabel, s);
            speakerMap.TryAdd(s.Identity, s);
            speakerMap.TryAdd(s.DisplayName, s);
        }

        var detectedSituations = await _chatAgent.DetectSituationsAsync(parsedMessages, labeledSpeakers, behaviors, ct);

        // Stage 4: chained deflection — transientsByIdentity threaded exactly as in the batch pipeline.
        var outSituations = new List<object>();
        int processedEvents = 0;

        foreach (var detSit in detectedSituations)
        {
            Dictionary<string, double[]>? transientsByIdentity = null;
            var outEvents = new List<object>();

            foreach (var evt in detSit.Events)
            {
                if (string.IsNullOrEmpty(evt.ActorSpeaker) || string.IsNullOrEmpty(evt.ObjectSpeaker))
                    continue;

                var actorSpeaker = speakerMap.GetValueOrDefault(evt.ActorSpeaker);
                var objectSpeaker = speakerMap.GetValueOrDefault(evt.ObjectSpeaker);
                if (actorSpeaker == null || objectSpeaker == null)
                {
                    _logger.LogWarning("Skipping event '{Behavior}': speaker mapping failed.", evt.Behavior);
                    continue;
                }

                var interaction = new Interaction
                {
                    Actor = new Person { Name = actorSpeaker.DisplayName, Identity = actorSpeaker.Identity, Gender = "average" },
                    Object = new Person { Name = objectSpeaker.DisplayName, Identity = objectSpeaker.Identity, Gender = "average" },
                    Behavior = evt.Behavior,
                    OriginalMessage = evt.OriginalMessage
                };

                try
                {
                    var result = await _processingService.CalculateInteractionAsync(interaction, transientsByIdentity);
                    interaction.Result = result;

                    transientsByIdentity = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        [interaction.Actor.Identity] = result.TransientActorEPA,
                        [interaction.Object.Identity] = result.TransientObjectEPA
                    };

                    outEvents.Add(new
                    {
                        Actor = new { interaction.Actor.Name, interaction.Actor.Identity, interaction.Actor.Modifier, interaction.Actor.Gender },
                        Object = new { interaction.Object.Name, interaction.Object.Identity, interaction.Object.Modifier, interaction.Object.Gender },
                        interaction.Behavior,
                        Result = new
                        {
                            result.TransientActorEPA,
                            result.TransientBehaviorEPA,
                            result.TransientObjectEPA,
                            result.ActorEmotionEPA,
                            result.ObjectEmotionEPA,
                            result.Deflection
                        }
                    });
                    processedEvents++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to calculate EPA for event '{Behavior}'.", evt.Behavior);
                }
            }

            outSituations.Add(new { Type = detSit.Name, Events = outEvents });
        }

        // Shape matches the MongoDB conversation export consumed by parse_agent.py.
        var output = new
        {
            Name = req.Name,
            DictionaryKey = dictKey,
            Situations = outSituations
        };

        _logger.LogInformation("Pipeline analyzed '{Name}': {Turns} turns -> {Events} events.",
            req.Name, parsedMessages.Count, processedEvents);

        // Serialize with PascalCase preserved (default System.Text.Json) so parse_agent.py reads it
        // identically to the export. Returning via Ok(output) would apply MVC camelCase.
        var json = JsonSerializer.Serialize(output);
        return Content(json, "application/json");
    }
}
