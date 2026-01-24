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

    public async Task CreateAsync(Conversation conversation)
    {
        await _collection.InsertOneAsync(conversation);
    }

    public async Task UpdateAsync(Conversation conversation)
    {
        await _collection.ReplaceOneAsync(c => c.Id == conversation.Id, conversation);
    }
}
