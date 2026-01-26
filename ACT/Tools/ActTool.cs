using ACT.Services;
using ACT.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
        private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;
        private readonly IConversationService _conversationService;

        public ActTool(IServiceProvider serviceProvider)
        {
            _logger = serviceProvider.GetRequiredService<ILogger<ActTool>>();
            _actService = serviceProvider.GetRequiredService<IActService>();
            _processingService = serviceProvider.GetRequiredService<IActProcessingService>();
            _chatAgent = serviceProvider.GetRequiredService<IChatAgent>();
            _monitor = serviceProvider.GetRequiredService<IActToolMonitor>();
            _httpContextAccessor = serviceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            _conversationService = serviceProvider.GetRequiredService<IConversationService>();
        }

        private string GetSessionId()
        {
            if (_httpContextAccessor.HttpContext?.Items.TryGetValue("McpSessionId", out var sessionIdDict) == true)
            {
               return sessionIdDict as string ?? "Unknown";
            }
            return _httpContextAccessor.HttpContext?.Connection?.Id ?? "Unknown";
        }

        private async Task<string> ResolveDictionaryKey(string? requestedKey) 
        {
             var dictionaries = await _actService.GetDictionariesAsync();
             if (!dictionaries.Any()) throw new InvalidOperationException("No dictionaries available");

             // 1. Requested
             if (!string.IsNullOrEmpty(requestedKey) && dictionaries.Any(d => d.Key.Equals(requestedKey, StringComparison.OrdinalIgnoreCase))) return requestedKey;
             
             // 2. Env
             var envKey = Environment.GetEnvironmentVariable("DEFAULT_DICTIONARY");
             if (!string.IsNullOrEmpty(envKey) && dictionaries.Any(d => d.Key.Equals(envKey, StringComparison.OrdinalIgnoreCase))) return envKey;

             // 3. Germany2007
             var germany = dictionaries.FirstOrDefault(d => d.Key.Equals("germany2007", StringComparison.OrdinalIgnoreCase));
             if (germany != null) return germany.Key;

             // 4. First
             return dictionaries.First().Key;
        }

        [McpServerTool, Description("Returns a simple guide on how to use this tool.")]
        public string GetUsageGuide()
        {
            return @"
# ACT Tool Usage Guide

This tool allows you to perform Affect Control Theory (ACT) analysis on your conversations.

## 1. Configuration
First, you must configure the session.
- Call `ConfigureSessionAsync(dictionaryKey)`
- If you don't provide a key, a default will be used (e.g. germany2007).

## 2. Conversation
You can create a named conversation or use the default one created/found during configuration.
- Call `CreateConversationAsync(name)` if you want to start fresh.

## 3. Recording Interactions
As you interact with the user, analyze the exchange and record ACT events.
- Call `RecordInteractionAsync` with the input, output, and the ACT event (Actor, Behavior, Object).
- You are usually the 'Assistant' and the user is 'User'.
- The tool will calculate EPA values and deflection for you.

## 4. Other Tools
- `ListDictionariesAsync`: See available dictionaries.
- `CalculateInteractionAsync`: Just calculate/simulate without recording.
";
        }

        [McpServerTool, Description("Configures the session with a specific dictionary or default.")]
        public async Task<string> ConfigureSessionAsync(
            [Description("The dictionary key to use (optional).")] string? dictionaryKey,
            CancellationToken ct)
        {
            var sessionId = GetSessionId();
            try
            {
                var resolvedKey = await ResolveDictionaryKey(dictionaryKey);
                
                // Find or create conversation
                var conversation = await _conversationService.GetLatestBySessionIdAsync(sessionId);
                if (conversation == null)
                {
                    conversation = await _conversationService.CreateAsync($"Session {DateTime.Now:g}", resolvedKey);
                    conversation.SessionId = sessionId;
                    await _conversationService.UpdateAsync(conversation);
                }
                else
                {
                    if (conversation.DictionaryKey != resolvedKey)
                    {
                        conversation.DictionaryKey = resolvedKey;
                        await _conversationService.UpdateAsync(conversation);
                    }
                }

                var result = $"Session configured. Dictionary: {resolvedKey}. Conversation ID: {conversation.Id}";
                _monitor.RecordCall(sessionId, nameof(ConfigureSessionAsync), dictionaryKey ?? "null", result, true);
                return result;
            }
            catch (Exception ex)
            {
                _monitor.RecordCall(sessionId, nameof(ConfigureSessionAsync), dictionaryKey ?? "null", "", false, ex.Message);
                throw;
            }
        }

        [McpServerTool, Description("Creates a new conversation for the current session.")]
        public async Task<string> CreateConversationAsync(
            [Description("Name of the conversation.")] string name,
            CancellationToken ct)
        {
             var sessionId = GetSessionId();
             try
             {
                 // Get current dictionary preference from latest convo or default
                 var previous = await _conversationService.GetLatestBySessionIdAsync(sessionId);
                 var dictKey = previous?.DictionaryKey ?? await ResolveDictionaryKey(null);

                 var conversation = await _conversationService.CreateAsync(name, dictKey);
                 conversation.SessionId = sessionId;
                 await _conversationService.UpdateAsync(conversation);
                 
                 var result = $"Created Conversation: {conversation.Id} ({conversation.Name})";
                 _monitor.RecordCall(sessionId, nameof(CreateConversationAsync), name, result, true);
                 return result;
             }
             catch (Exception ex)
             {
                 _monitor.RecordCall(sessionId, nameof(CreateConversationAsync), name, "", false, ex.Message);
                 throw;
             }
        }

        [McpServerTool, Description("Records an interaction and calculates EPA/Deflection.")]
        public async Task<string> RecordInteractionAsync(
            [Description("The user's input text.")] string userInput,
            [Description("The agent's output text.")] string agentOutput,
            [Description("The actor in the ACT event.")] string actor,
            [Description("The behavior in the ACT event.")] string behavior,
            [Description("The object in the ACT event.")] string objectIdentity,
            [Description("Description of the situation (optional).")] string? situation,
            [Description("Label for the user (default: User).")] string userLabel = "User",
            [Description("Label for the agent (default: Assistant).")] string agentLabel = "Assistant",
            CancellationToken ct = default)
        {
            var sessionId = GetSessionId();
            try
            {
                var conversation = await _conversationService.GetLatestBySessionIdAsync(sessionId);
                if (conversation == null)
                {
                     // Auto-create if not exists
                     await ConfigureSessionAsync(null, ct);
                     conversation = await _conversationService.GetLatestBySessionIdAsync(sessionId);
                }

                if (conversation == null) throw new InvalidOperationException("Failed to establish conversation.");

                // Ensure Persons exist
                if (!conversation.Persons.Any(p => p.Name == userLabel)) 
                {
                    await _conversationService.AddPersonAsync(conversation.Id, new Person { Name = userLabel, Identity = "Client" });
                }
                if (!conversation.Persons.Any(p => p.Name == agentLabel)) 
                {
                    await _conversationService.AddPersonAsync(conversation.Id, new Person { Name = agentLabel, Identity = "Consultant" });
                }

                // Ensure Situation
                var sit = conversation.Situations.LastOrDefault();
                if (sit == null || (!string.IsNullOrEmpty(situation) && sit.Type != situation))
                {
                    // Create new situation via service
                    // Note: This returns the NEW situation object, but it is attached to the conversation object in DB
                    sit = await _conversationService.AddSituationAsync(conversation.Id, situation ?? "Interaction");
                    
                    // Note: 'conversation' variable here is now stale regarding situations list potentially, 
                    // but AddEventAsync fetches fresh conversation by ID, so it is fine.
                }

                // Create Interaction (No Result calculated yet)
                var interaction = new Interaction
                {
                    Actor = new Person { Identity = actor, Name = actor },
                    Behavior = behavior,
                    Object = new Person { Identity = objectIdentity, Name = objectIdentity }
                };
                
                // Add Event (Delegates calculation to Service)
                await _conversationService.AddEventAsync(conversation.Id, sit, interaction);

                // Check result
                // Since interaction was passed by reference, it should be populated?
                // Wait, AddEventAsync runs remotely? No, it's in-process service.
                // It modifies the object.
                
                ActEventResultDto resultDto = new ActEventResultDto();
                if (interaction.Result != null)
                {
                    resultDto = new ActEventResultDto
                    {
                        Actor = actor,
                        Behavior = behavior,
                        Object = objectIdentity,
                        Deflection = interaction.Result.Deflection,
                        ActorEPA = interaction.Result.TransientActorEPA,
                        BehaviorEPA = interaction.Result.TransientBehaviorEPA,
                        ObjectEPA = interaction.Result.TransientObjectEPA
                    };
                }
                else
                {
                    resultDto.Error = "Calculation failed or was skipped.";
                }

                var result = ToonSerializer.Serialize(resultDto);
                 _monitor.RecordCall(sessionId, nameof(RecordInteractionAsync), $"{actor}-{behavior}-{objectIdentity}", result, true);
                return result;
            }
             catch (Exception ex)
            {
                 _monitor.RecordCall(sessionId, nameof(RecordInteractionAsync), $"{actor}-{behavior}-{objectIdentity}", "", false, ex.Message);
                 throw;
            }
        }


        [McpServerTool, Description("Returns a list of all available act dictionaries")]
        public async Task<string> ListDictionariesAsync(CancellationToken ct)
        {
            var sessionId = GetSessionId();
            try
            {
                var dictionaries = await _actService.GetDictionariesAsync(ct);
                var result = ToonSerializer.Serialize(dictionaries);
                _monitor.RecordCall(sessionId, nameof(ListDictionariesAsync), "", result, true);
                return result;
            }
            catch (Exception ex)
            {
                _monitor.RecordCall(sessionId, nameof(ListDictionariesAsync), "", "", false, ex.Message);
                throw;
            }
        }

        [McpServerTool, Description("Analyzes a conversation script and returns ACT events with EPA/Deflection values.")]
        public async Task<string> AnalyzeConversationAsync(
            [Description("The full text of the conversation to analyze.")] string conversationText, 
            CancellationToken ct)
        {
            var sessionId = GetSessionId();
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
            _monitor.RecordCall(sessionId, nameof(AnalyzeConversationAsync), conversationText, resultKey, true);
            return resultKey;
            }
            catch (Exception ex)
            {
                _monitor.RecordCall(sessionId, nameof(AnalyzeConversationAsync), conversationText, "", false, ex.Message);
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
             var sessionId = GetSessionId();
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
                _monitor.RecordCall(sessionId, nameof(CalculateInteractionAsync), $"Actor: {actor}, Behavior: {behavior}, Object: {objectIdentity}", serializedResult, true);
                return serializedResult;
            }
            catch (Exception ex)
            {
                var errorResult = ToonSerializer.Serialize(new { error = ex.Message });
                _monitor.RecordCall(sessionId, nameof(CalculateInteractionAsync), $"Actor: {actor}, Behavior: {behavior}, Object: {objectIdentity}", errorResult, false, ex.Message);
                return errorResult;
            }
        }

        [McpServerTool, Description("Proposes next actions by calculating deflection for all available behaviors.")]
        public async Task<string> SuggestNextActionsAsync(
            [Description("The identity of the actor (agent).")] string actor,
            [Description("The identity of the object (user).")] string objectIdentity,
            [Description("Limit the number of suggestions.")] int limit = 10,
            CancellationToken ct = default)
        {
            var sessionId = GetSessionId();
            try
            {
                var conversation = await _conversationService.GetLatestBySessionIdAsync(sessionId);
                var dictKey = conversation?.DictionaryKey ?? await ResolveDictionaryKey(null);

                var suggestions = await _actService.SuggestActionsAsync(actor, objectIdentity, dictKey, "male", ct);
                
                var top = suggestions.Take(limit).ToList();
                var result = ToonSerializer.Serialize(top);
                
                _monitor.RecordCall(sessionId, nameof(SuggestNextActionsAsync), $"{actor}-{objectIdentity}", $"{top.Count} suggestions", true);
                return result;
            }
            catch (Exception ex)
            {
                 _monitor.RecordCall(sessionId, nameof(SuggestNextActionsAsync), $"{actor}-{objectIdentity}", "", false, ex.Message);
                 throw;
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
