using ACT.Models;
using System.Collections.Generic;

namespace ACT.Services;

public interface ISituationService
{
    Situation CreateSituation(string type = "Empty");
    void AddPerson(Situation situation, string name, string identity, string gender = "avg");
    Interaction CreateInteraction(Person actor, Person obj, string behavior);
}

public class SituationService : ISituationService
{
    public Situation CreateSituation(string type = "Empty")
    {
        return new Situation { Type = type };
    }

    public void AddPerson(Situation situation, string name, string identity, string gender = "avg")
    {
        var person = new Person
        {
            Name = name,
            Identity = identity,
            Gender = gender
        };
        situation.Persons.Add(person);
    }

    public Interaction CreateInteraction(Person actor, Person obj, string behavior)
    {
        return new Interaction
        {
            Actor = actor,
            Object = obj,
            Behavior = behavior
        };
    }
}
