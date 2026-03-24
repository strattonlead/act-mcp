namespace ACT.Models;

public class Person
{
    public string Name { get; set; } = string.Empty;
    public string Identity { get; set; } = string.Empty;
    public string Modifier { get; set; } = "_";
    public string Gender { get; set; } = "average"; // male, female, average
    
    // We'll store EPA as a simple array or object for now, 
    // waiting for specific vector implementation details if needed.
    // For now, let's use double[] for [E, P, A]
    public double[]? EPA { get; set; }
}
