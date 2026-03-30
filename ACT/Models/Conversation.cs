using System;
using System.Collections.Generic;

namespace ACT.Models;

public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Conversation";
    public string DictionaryKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? SessionId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid? AttachedFileId { get; set; }

    /// <summary>
    /// Tracks the result of the last auto evaluation run.
    /// null = never evaluated, "Success" = all turns processed,
    /// "Partial: 8/12 turns" = some turns failed, "Failed: ..." = error details.
    /// </summary>
    public string? AutoEvalStatus { get; set; }
    public DateTime? AutoEvalAt { get; set; }

    /// <summary>Total dialog turns (messages) parsed from the input file.</summary>
    public int TotalTurns { get; set; }

    /// <summary>Number of turns that were successfully mapped to events.</summary>
    public int ProcessedTurns { get; set; }

    /// <summary>Raw extracted text from the attached file.</summary>
    public string? RawText { get; set; }

    /// <summary>Parsed message turns from the raw text.</summary>
    public List<ParsedMessage> ParsedMessages { get; set; } = new();

    // Participants in the conversation
    public List<Person> Persons { get; set; } = new();

    // Sequence of situations
    public List<Situation> Situations { get; set; } = new();
}
