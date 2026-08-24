namespace MotionInput.Core.Output;

/// <summary>Data available while resolving placeholder tokens (e.g. $motion_final, $attack) into primitive outputs.</summary>
public sealed class OutputContext
{
    public int? StartDirection { get; init; }

    public int? FinalDirection { get; init; }

    /// <summary>The controller button token (e.g. "x") the triggering attack role resolves to, for $attack.</summary>
    public string? AttackControllerButton { get; init; }

    /// <summary>
    /// Role name (e.g. "light", "s1") -> the controller button that role currently resolves to.
    /// Lets an output token name any bound role directly (e.g. "S1") rather than just the one
    /// that's currently triggering. Case-insensitive.
    /// </summary>
    public IReadOnlyDictionary<string, string>? RoleButtons { get; init; }
}
