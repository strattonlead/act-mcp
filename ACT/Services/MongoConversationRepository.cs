using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ACT.Models;
using MongoDB.Driver;

namespace ACT.Services;

public class MongoConversationRepository : IConversationRepository
{
    private readonly IMongoCollection<Conversation> _collection;

    public MongoConversationRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<Conversation>("conversations");
    }

    public async Task<List<Conversation>> GetAllAsync()
    {
        return await _collection.Find(_ => true).ToListAsync();
    }

    public async Task<Conversation?> GetByIdAsync(Guid id)
    {
        return await _collection.Find(c => c.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Conversation> CreateAsync(Conversation conversation)
    {
        await _collection.InsertOneAsync(conversation);
        return conversation;
    }

    public async Task UpdateAsync(Conversation conversation)
    {
        await _collection.ReplaceOneAsync(c => c.Id == conversation.Id, conversation);
    }

    public async Task<Conversation?> GetLatestBySessionIdAsync(string sessionId)
    {
        return await _collection.Find(c => c.SessionId == sessionId)
            .SortByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await _collection.DeleteOneAsync(c => c.Id == id);
    }
}
