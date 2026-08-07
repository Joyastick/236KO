namespace MotionInput.Core.Motion;

/// <summary>A named numpad-notation motion, e.g. "qcf" = [2,3,6].</summary>
public sealed class MotionDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Required numpad directions (1-9, excluding 5) in order. First = start, last = final direction.</summary>
    public List<int> Sequence { get; set; } = new();

    /// <summary>Allow a numpad-adjacent direction (e.g. 3 for a required 2 or 6) to satisfy a step, matching modern fighting-game diagonal leniency.</summary>
    public bool AllowDiagonalSkip { get; set; } = true;

    /// <summary>Overrides the global max total sequence time, in milliseconds. Null = use global default.</summary>
    public int? MaxSequenceMs { get; set; }

    /// <summary>Overrides the global max gap between consecutive required steps, in milliseconds. Null = use global default.</summary>
    public int? MaxGapMs { get; set; }
}
