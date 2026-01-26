using System;
using System.Threading.Tasks;
using ACT.Models;
using MongoDB.Driver;

namespace ACT.Services;

public class MongoFileRepository : IFileRepository
{
    private readonly IMongoCollection<UploadedFile> _collection;

    public MongoFileRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<UploadedFile>("uploaded_files");
    }

    public async Task<UploadedFile?> GetByIdAsync(Guid id)
    {
        return await _collection.Find(f => f.Id == id).FirstOrDefaultAsync();
    }

    public async Task CreateAsync(UploadedFile file)
    {
        await _collection.InsertOneAsync(file);
    }

    public async Task DeleteAsync(Guid id)
    {
        await _collection.DeleteOneAsync(f => f.Id == id);
    }
}
