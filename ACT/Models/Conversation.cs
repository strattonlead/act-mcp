using System;
using System.Collections.Generic;

namespace ACT.Models;

public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Conversation";
    public string DictionaryKey { get; set; } = string.Empty;
    
    // Participants in the conversation
    public List<Person> Persons { get; set; } = new();
    
    // Sequence of situations
    public List<Situation> Situations { get; set; } = new();
}
