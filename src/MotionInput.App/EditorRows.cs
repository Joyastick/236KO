namespace MotionInput.App;

/// <summary>DataGrid row for a <see cref="MotionInput.Core.Motion.MotionDefinition"/>.</summary>
public sealed class MotionRow
{
    public string Name { get; set; } = string.Empty;
    public string SequenceText { get; set; } = string.Empty;
    public bool AllowDiagonalSkip { get; set; } = true;
    public string MaxSequenceMsText { get; set; } = string.Empty;
    public string MaxGapMsText { get; set; } = string.Empty;
}

/// <summary>DataGrid row for a "name -> comma separated values" dictionary entry (bindings, attack outputs, key outputs).</summary>
public sealed class KeyValueRow
{
    public string Role { get; set; } = string.Empty;
    public string ValuesText { get; set; } = string.Empty;
}

/// <summary>DataGrid row for one (motion, attack role) -> tokens entry of MotionAttackOutputs.</summary>
public sealed class MotionOutputRow
{
    public string Motion { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string TokensText { get; set; } = string.Empty;
}

/// <summary>DataGrid row for a HidHide-visible device.</summary>
public sealed class HidHideDeviceRow
{
    public bool IsCloaked { get; set; }
    public string FriendlyName { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
}
