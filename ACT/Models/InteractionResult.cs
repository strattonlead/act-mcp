namespace ACT.Models;

public class InteractionResult
{
    public double[] TransientActorEPA { get; set; } = new double[3];
    public double[] TransientBehaviorEPA { get; set; } = new double[3];
    public double[] TransientObjectEPA { get; set; } = new double[3];
    public double[] ActorEmotionEPA { get; set; } = new double[3];
    public double[] ObjectEmotionEPA { get; set; } = new double[3];
    public double Deflection { get; set; }
}
