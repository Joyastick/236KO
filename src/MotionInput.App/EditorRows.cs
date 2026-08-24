using System.ComponentModel;
using System.Runtime.CompilerServices;

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

/// <summary>
/// DataGrid row for one (motion, attack role) -> [direction, output role, output role 2] entry of
/// MotionAttackOutputs, e.g. motion "qcf" + Role "light" -> Direction "6" + OutputRole "S1". A
/// second output role is optional, for combos that press two roles' buttons at once.
/// </summary>
public sealed class MotionOutputRow
{
    public string Motion { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string OutputRole { get; set; } = string.Empty;
    public string OutputRole2 { get; set; } = string.Empty;
}

/// <summary>A selectable entry in the Direction dropdown: a friendly label plus the raw output token it writes.</summary>
public sealed record DirectionOutputOption(string Label, string Token);

/// <summary>The fixed vocabulary of button-style 2XKO roles, shared between the Bindings tab and the Motion + Attack Outputs role dropdowns.</summary>
public static class ButtonRoles
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "light", "medium", "heavy", "s1", "s2", "tag", "start", "select", "dash", "break", "parry",
    };
}

/// <summary>
/// Row for the Bindings tab's press-to-bind wizard, one per 2XKO input (the four directions plus
/// Light/Medium/Heavy/S1/S2/Tag). Directions toggle the existing D-Pad/stick source checkboxes;
/// everything else writes straight into the Attack Bindings/Attack Outputs grids as a passthrough.
/// </summary>
public sealed class BindingRow : INotifyPropertyChanged
{
    public required string Role { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsDirection { get; init; }

    private string _boundText = "Not bound";
    public string BoundText
    {
        get => _boundText;
        set { _boundText = value; OnChanged(); }
    }

    private bool _isListening;
    public bool IsListening
    {
        get => _isListening;
        set { _isListening = value; OnChanged(); OnChanged(nameof(ListenButtonText)); }
    }

    public string ListenButtonText => IsListening ? "Press now…" : "Listen";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
