namespace MotionInput.Core.Motion;

/// <summary>Global buffer/leniency tuning, overridable per-motion where noted.</summary>
public sealed record MotionLeniencySettings
{
    /// <summary>Max total time from the first required step to the last, in milliseconds.</summary>
    public int MaxSequenceMs { get; set; } = 500;

    /// <summary>Max time allowed between two consecutive required steps, in milliseconds.</summary>
    public int MaxGapMs { get; set; } = 250;

    /// <summary>How long after a motion completes the matcher keeps watching for an attack button to combine with it.</summary>
    public int AttackWindowMs { get; set; } = 300;

    /// <summary>Minimum time before the same motion can be recognized again, to avoid duplicate triggers off overlapping history.</summary>
    public int MotionCooldownMs { get; set; } = 150;
}
