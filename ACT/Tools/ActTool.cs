using ACT.Services;
using ACT.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ToonSharp;

namespace ACT.Tools
{
    [McpServerToolType, Description("Tools for ACT (affect control theory)")]
    public class ActTool
    {
        private readonly ILogger<ActTool> _logger;
        private readonly IActService _actService;
        private readonly IActProcessingService _processingService;
        private readonly IChatAgent _chatAgent;
        private readonly IActToolMonitor _monitor;

        public ActTool(IServiceProvider serviceProvider)
        {
            _logger = serviceProvider.GetRequiredService<ILogger<ActTool>>();
            _actService = serviceProvider.GetRequiredService<IActService>();
            _processingService = serviceProvider.GetRequiredService<IActProcessingService>();
            _chatAgent = serviceProvider.GetRequiredService<IChatAgent>();
            _monitor = serviceProvider.GetRequiredService<IActToolMonitor>();
        }

        [McpServerTool, Description("Returns a list of all available act dictionaries")]
        public async Task<string> ListDictionariesAsync(CancellationToken ct)
        {
            try
            {
                var dictionaries = await _actService.GetDictionariesAsync(ct);
                var result = ToonSerializer.Serialize(dictionaries);
                _monitor.RecordCall(nameof(ListDictionariesAsync), "", result, true);
                return result;
            }
            catch (Exception ex)
            {
                _monitor.RecordCall(nameof(ListDictionariesAsync), "", "", false, ex.Message);
                throw;
            }
        }

        [McpServerTool, Description("Analyzes a conversation script and returns ACT events with EPA/Deflection values.")]
        public async Task<string> AnalyzeConversationAsync(
            [Description("The full text of the conversation to analyze.")] string conversationText, 
            CancellationToken ct)
        {
            try
            {
                // 1. Extract events using LLM
                var extractionResult = await _chatAgent.ExtractActEventsAsync(conversationText, ct);
            
            // 2. Parse extraction result
            var lines = extractionResult.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var analysisResult = new ActAnalysisResultDto();
            double totalDeflection = 0;
            int count = 0;

            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length != 3) continue;

                var evt = new ActEventResultDto
                {
                    Actor = parts[0].Trim(),
                    Behavior = parts[1].Trim(),
                    Object = parts[2].Trim()
                };

                try
                {
                    // Calculate
                    // We need to create dummy Persons for the calculation service
                    var interaction = new Interaction
                    {
                        Actor = new Person { Identity = evt.Actor }, // Assuming simple identity mapping
                        Behavior = evt.Behavior,
                        Object = new Person { Identity = evt.Object }
                    };
                    
                    var result = await _processingService.CalculateInteractionAsync(interaction);
                    
                    evt.Deflection = result.Deflection;
                    evt.ActorEPA = result.TransientActorEPA;
                    evt.BehaviorEPA = result.TransientBehaviorEPA;
                    evt.ObjectEPA = result.TransientObjectEPA;

                    totalDeflection += result.Deflection;
                    count++;
                }
                catch (Exception ex)
                {
                    evt.Error = ex.Message;
                    _logger.LogError(ex, "Failed to calculate ACT for event: {Line}", line);
                }
                
                analysisResult.Events.Add(evt);
            }

            if (count > 0)
                analysisResult.AverageDeflection = totalDeflection / count;

            var resultKey = ToonSerializer.Serialize(analysisResult);
            _monitor.RecordCall(nameof(AnalyzeConversationAsync), conversationText, resultKey, true);
            return resultKey;
            }
            catch (Exception ex)
            {
                _monitor.RecordCall(nameof(AnalyzeConversationAsync), conversationText, "", false, ex.Message);
                throw;
            }
        }

        [McpServerTool, Description("Calculates EPA and Deflection for a single ACT interaction.")]
        public async Task<string> CalculateInteractionAsync(
            [Description("The identity of the actor (e.g., 'teacher').")] string actor,
            [Description("The behavior performed (e.g., 'advise').")] string behavior,
            [Description("The identity of the object (e.g., 'student').")] string objectIdentity,
            CancellationToken ct)
        {
             var interaction = new Interaction
            {
                Actor = new Person { Identity = actor },
                Behavior = behavior,
                Object = new Person { Identity = objectIdentity }
            };

            try
            {
                var result = await _processingService.CalculateInteractionAsync(interaction);
                var serializedResult = ToonSerializer.Serialize(result);
                _monitor.RecordCall(nameof(CalculateInteractionAsync), $"Actor: {actor}, Behavior: {behavior}, Object: {objectIdentity}", serializedResult, true);
                return serializedResult;
            }
            catch (Exception ex)
            {
                var errorResult = ToonSerializer.Serialize(new { error = ex.Message });
                _monitor.RecordCall(nameof(CalculateInteractionAsync), $"Actor: {actor}, Behavior: {behavior}, Object: {objectIdentity}", errorResult, false, ex.Message);
                return errorResult;
            }
        }
    }

    public static class ActToolDI
    {
        public static void AddActTool(this IServiceCollection services)
        {
            services.AddScoped<ActTool>();
        }
    }
}
