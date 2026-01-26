using ACT.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ACT.Services;

public interface IBatchFileStatusService
{
    void Add(BatchFileStatus status);
    BatchFileStatus? GetByConversationId(Guid conversationId);
    List<BatchFileStatus> GetAll();
    event Action? OnChange;
    void NotifyStateChanged();
}

public class BatchFileStatusService : IBatchFileStatusService
{
    private readonly ConcurrentBag<BatchFileStatus> _statuses = new();

    public event Action? OnChange;

    public void Add(BatchFileStatus status)
    {
        _statuses.Add(status);
        NotifyStateChanged();
    }

    public List<BatchFileStatus> GetAll()
    {
        return _statuses.ToList();
    }

    public BatchFileStatus? GetByConversationId(Guid conversationId)
    {
        return _statuses.FirstOrDefault(s => s.ConversationId == conversationId);
    }

    public void NotifyStateChanged()
    {
        OnChange?.Invoke();
    }
}
