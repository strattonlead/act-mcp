using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ACT.Models;

namespace ACT.Services;

public interface IConversationService
{
    Task<List<Conversation>> GetAllAsync();
    Task<Conversation?> GetByIdAsync(Guid id);
    Task<Conversation?> GetLatestBySessionIdAsync(string sessionId);
    Task<Conversation> CreateAsync(string name, string dictionaryKey);
    Task AddPersonAsync(Guid conversationId, Person person);
    Task ClearPersonsAsync(Guid conversationId);
    Task<Situation> AddSituationAsync(Guid conversationId, string type);
    Task AddEventAsync(Guid conversationId, Situation situation, Interaction interaction);
    Task UpdateAsync(Conversation conversation);
    Task DeleteAsync(Guid id);
}

public class ConversationService : IConversationService
{
    private readonly IConversationRepository _repository;
    private readonly IFileRepository _fileRepository;
    private readonly IS3Service _s3Service;
    private readonly IActProcessingService _actProcessingService;

    public ConversationService(IConversationRepository repository, IFileRepository fileRepository, IS3Service s3Service, IActProcessingService actProcessingService)
    {
        _repository = repository;
        _fileRepository = fileRepository;
        _s3Service = s3Service;
        _actProcessingService = actProcessingService;
    }

    public async Task<List<Conversation>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<Conversation?> GetByIdAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<Conversation?> GetLatestBySessionIdAsync(string sessionId)
    {
        return await _repository.GetLatestBySessionIdAsync(sessionId);
    }

    public async Task<Conversation> CreateAsync(string name, string dictionaryKey)
    {
        var conv = new Conversation
        {
            Name = name,
            DictionaryKey = dictionaryKey
        };
        await _repository.CreateAsync(conv);
        return conv;
    }

    public async Task AddPersonAsync(Guid conversationId, Person person)
    {
        var conv = await _repository.GetByIdAsync(conversationId);
        if (conv == null) return;
        
        // Check duplication by name
        if (!conv.Persons.Any(p => p.Name == person.Name))
        {
            conv.Persons.Add(person);
            await _repository.UpdateAsync(conv);
        }
    }

    public async Task ClearPersonsAsync(Guid conversationId)
    {
        var conv = await _repository.GetByIdAsync(conversationId);
        if (conv != null)
        {
            conv.Persons.Clear();
            await _repository.UpdateAsync(conv);
        }
    }

    public async Task<Situation> AddSituationAsync(Guid conversationId, string type)
    {
        var conv = await _repository.GetByIdAsync(conversationId);
        if (conv == null) throw new ArgumentException("Conversation not found");

        var situation = new Situation { Type = type };
        conv.Situations.Add(situation);
        
        await _repository.UpdateAsync(conv);
        return situation;
    }

    public async Task AddEventAsync(Guid conversationId, Situation situation, Interaction interaction)
    {
        var conv = await _repository.GetByIdAsync(conversationId);
        if (conv == null) throw new ArgumentException("Conversation not found");

        var targetSituation = conv.Situations.FirstOrDefault(s => s.Id == situation.Id);
        if (targetSituation == null)
        {
             // Fallback: try to match by type if ID not present in passed object (backward compat?)
             // But valid situations should have IDs now.
             throw new ArgumentException("Situation not found in conversation");
        }

        if (interaction.Result == null)
        {
            // For transient chaining: build identity→transient map from previous events.
            // This correctly handles actor/object role swaps between events.
            Dictionary<string, double[]>? transientsByIdentity = null;
            if (targetSituation.Events.Count > 0)
            {
                var lastEvent = targetSituation.Events.LastOrDefault();
                if (lastEvent?.Result != null)
                {
                    transientsByIdentity = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        [lastEvent.Actor.Identity] = lastEvent.Result.TransientActorEPA,
                        [lastEvent.Object.Identity] = lastEvent.Result.TransientObjectEPA
                    };
                }
            }
            interaction.Result = await _actProcessingService.CalculateInteractionAsync(interaction, transientsByIdentity);
        }

        targetSituation.Events.Add(interaction);
        await _repository.UpdateAsync(conv);
    }

    public async Task UpdateAsync(Conversation conversation)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(conversation);
    }

    public async Task DeleteAsync(Guid id)
    {
        var conversation = await _repository.GetByIdAsync(id);
        if (conversation != null) 
        {
            // Allow repository to handle cascading deletes if needed, but for files we do it here or in Repo?
            // Service layer seems appropriate for coordinating Entity + File + S3 deletion.
            
            if (conversation.AttachedFileId.HasValue)
            {
                var file = await _fileRepository.GetByIdAsync(conversation.AttachedFileId.Value);
                if (file != null)
                {
                    try 
                    {
                        await _s3Service.DeleteFileAsync(file.S3BucketName, file.S3Key);
                    }
                    catch 
                    { 
                        // Log warning: S3 deletion failed, but proceed with DB cleanup 
                    }
                    await _fileRepository.DeleteAsync(file.Id);
                }
            }

            await _repository.DeleteAsync(id);
        }
    }
}
