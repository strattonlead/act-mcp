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
    Task<Conversation> CreateAsync(string name, string dictionaryKey);
    Task AddPersonAsync(Guid conversationId, Person person);
    Task<Situation> AddSituationAsync(Guid conversationId, string type);
    Task AddEventAsync(Guid conversationId, Situation situation, Interaction interaction);
    Task UpdateAsync(Conversation conversation);
}

public class ConversationService : IConversationService
{
    private readonly IConversationRepository _repository;

    public ConversationService(IConversationRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<Conversation>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<Conversation?> GetByIdAsync(Guid id)
    {
        return await _repository.GetByIdAsync(id);
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

        targetSituation.Events.Add(interaction);
        await _repository.UpdateAsync(conv);
    }

    public async Task UpdateAsync(Conversation conversation)
    {
        await _repository.UpdateAsync(conversation);
    }
}
