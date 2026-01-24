using System;

namespace ACT.Models;

public enum BatchFileState
{
    Pending,
    Uploading,
    Parsing,
    CreatingConversation,
    Analyzing,
    Completed,
    Failed
}

public class BatchFileStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public BatchFileState State { get; set; } = BatchFileState.Pending;
    public int Progress { get; set; } = 0;
    public string StatusMessage { get; set; } = "Pending";
    public Guid? ConversationId { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Internal use for the file content
    // We won't store the stream here permanently, but we might pass it around or store the extracted text.
    public string? ExtractedText { get; set; }
}
