using System;
using System.Collections.Generic;

namespace ACT.Models;

public class Situation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "Empty";
    public List<Person> Persons { get; set; } = new();
    public List<Interaction> Events { get; set; } = new();
}
