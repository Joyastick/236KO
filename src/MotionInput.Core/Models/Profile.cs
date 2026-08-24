using MotionInput.Core.Motion;

namespace MotionInput.Core.Models;

/// <summary>
/// Everything needed to go from raw controller input to emulated output: which physical inputs
/// count as directions/attacks, which motions to look for, buffer/leniency tuning, and what to
/// press on the virtual pad when a motion (optionally plus an attack) is recognized.
/// </summary>
public sealed class Profile
{
    public string Name { get; set; } = "Default";

    public ControllerInputSettings ControllerInput { get; set; } = new();

    public MotionLeniencySettings Leniency { get; set; } = new();

    /// <summary>Attack role (e.g. "light") -> physical input ids that trigger it (e.g. ["x"]).</summary>
    public Dictionary<string, List<string>> AttackBindings { get; set; } = new()
    {
        ["light"] = new() { "x" },
        ["medium"] = new() { "y" },
        ["heavy"] = new() { "b" },
    };

    /// <summary>Motions in priority order — earlier entries are matched first when inputs could satisfy more than one.</summary>
    public List<MotionDefinition> Motions { get; set; } = new();

    /// <summary>Motion name -> attack role -> output tokens, fired when the attack lands inside the motion's attack window.</summary>
    public Dictionary<string, Dictionary<string, List<string>>> MotionAttackOutputs { get; set; } = new();

    /// <summary>Attack role -> output tokens, fired when the attack is pressed with no motion preceding it.</summary>
    public Dictionary<string, List<string>> AttackOutputs { get; set; } = new();

    /// <summary>Physical input id -> output tokens, for direct passthrough/remap of any other button (e.g. "start" -> ["controller:start"]).</summary>
    public Dictionary<string, List<string>> KeyOutputs { get; set; } = new();
}
