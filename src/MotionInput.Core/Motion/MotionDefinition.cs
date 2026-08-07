namespace MotionInput.Core.Motion;

/// <summary>A named numpad-notation motion, e.g. "qcf" = [2,3,6].</summary>
public sealed class MotionDefinition
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Required numpad directions (1-9, excluding 5) in order. First = start, last = final direction.</summary>
    public List<int> Sequence { get; set; } = new();

    /// <summary>
    /// Allow a numpad-adjacent direction (e.g. 3 for a required 2 or 6) to satisfy a step other
    /// than the last one, matching modern fighting-game diagonal leniency for the roll leading up
    /// to a motion. Never applies to the final required direction — that one is always matched
    /// exactly, so motions that share directions in different orders (dp [6,2,3] vs qcf [2,3,6])
    /// don't bleed into each other.
    /// </summary>
    public bool AllowDiagonalSkip { get; set; } = true;

    /// <summary>Overrides the global max total sequence time, in milliseconds. Null = use global default.</summary>
    public int? MaxSequenceMs { get; set; }

    /// <summary>Overrides the global max gap between consecutive required steps, in milliseconds. Null = use global default.</summary>
    public int? MaxGapMs { get; set; }
}
