using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ACT.Models;

namespace ACT.Services;

public interface IConversationRepository
{
    Task<List<Conversation>> GetAllAsync();
    Task<Conversation?> GetByIdAsync(Guid id);
    Task CreateAsync(Conversation conversation);
    Task UpdateAsync(Conversation conversation);
}
