using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ACT.Models;

public class ActEventDto
{
    [JsonPropertyName("actor")]
    public string Actor { get; set; }

    [JsonPropertyName("behavior")]
    public string Behavior { get; set; }

    [JsonPropertyName("object")]
    public string Object { get; set; }
}

public class ActAnalysisResultDto
{
    [JsonPropertyName("events")]
    public List<ActEventResultDto> Events { get; set; } = new();

    [JsonPropertyName("average_deflection")]
    public double AverageDeflection { get; set; }
}

public class ActEventResultDto : ActEventDto
{
    [JsonPropertyName("deflection")]
    public double Deflection { get; set; }

    [JsonPropertyName("actor_epa")]
    public double[] ActorEPA { get; set; }

    [JsonPropertyName("behavior_epa")]
    public double[] BehaviorEPA { get; set; }

    [JsonPropertyName("object_epa")]
    public double[] ObjectEPA { get; set; }
    
    [JsonPropertyName("error")]
    public string Error { get; set; }
}
