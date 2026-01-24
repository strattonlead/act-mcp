namespace ACT.Models;

public class Interaction
{
    public Person Actor { get; set; } = new();
    public Person Object { get; set; } = new();
    public string Behavior { get; set; } = string.Empty;
    public InteractionResult? Result { get; set; }

    public override string ToString()
    {
        // Format: Person 1[_,student],requests somethong from,Person 2[_,assistant]
        return $"{FormatPerson(Actor)},{Behavior},{FormatPerson(Object)}";
    }

    private string FormatPerson(Person person)
    {
        // Assuming Modifier is "_" if empty/default
        string modifier = string.IsNullOrWhiteSpace(person.Modifier) ? "_" : person.Modifier;
        return $"{person.Name}[{modifier},{person.Identity}]";
    }
}
