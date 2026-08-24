namespace MotionInput.Core.Output;

/// <summary>
/// Turns a profile's output token strings into concrete <see cref="PrimitiveOutput"/>s.
/// Supported tokens:
///   1-9                            dpad press(es) for that numpad direction (diagonals press two buttons); 5 = neutral (no press)
///   &lt;role name&gt;               whatever controller button the named role (e.g. "S1", from AttackBindings) currently outputs
///   controller:&lt;button&gt;         literal controller button/dpad press (a, b, x, y, lb, rb, lt, rt, start, back, ls, rs, dpad_up/down/left/right)
///   controller_direction:&lt;1-9&gt;   same as the bare digit form, kept for explicitness
///   $controller_motion_final       dpad press(es) for the matched motion's final direction
///   $controller_motion_start       dpad press(es) for the matched motion's starting direction
///   $attack                        the controller button the triggering attack role resolves to
///   anything else                  treated as a literal keyboard key name
///
/// This lets a combo be written role-first, e.g. a motion's "236" (qcf) + "Light" attack outputting
/// "5 + S1" — force the d-pad to neutral and press whatever button S1 is currently bound to.
/// </summary>
public static class OutputResolver
{
    public static IReadOnlyList<PrimitiveOutput> Resolve(IEnumerable<string> tokens, OutputContext context)
    {
        var result = new List<PrimitiveOutput>();
        foreach (var token in tokens)
        {
            ResolveToken(token, context, result);
        }
        return result;
    }

    private static void ResolveToken(string token, OutputContext context, List<PrimitiveOutput> result)
    {
        if (token.StartsWith("controller:", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new PrimitiveOutput(PrimitiveOutputKind.ControllerButton, token["controller:".Length..].ToLowerInvariant()));
            return;
        }

        if (token.StartsWith("controller_direction:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(token["controller_direction:".Length..], out var explicitNumpad))
            {
                AddDirection(explicitNumpad, result);
            }
            return;
        }

        if (token.Length == 1 && token[0] is >= '1' and <= '9')
        {
            AddDirection(token[0] - '0', result);
            return;
        }

        switch (token)
        {
            case "$controller_motion_final":
                if (context.FinalDirection is { } finalDir)
                {
                    AddDirection(finalDir, result);
                }
                return;
            case "$controller_motion_start":
                if (context.StartDirection is { } startDir)
                {
                    AddDirection(startDir, result);
                }
                return;
            case "$attack":
                if (context.AttackControllerButton is { } attackButton)
                {
                    result.Add(new PrimitiveOutput(PrimitiveOutputKind.ControllerButton, attackButton));
                }
                return;
        }

        if (context.RoleButtons is not null && context.RoleButtons.TryGetValue(token, out var roleButton))
        {
            result.Add(new PrimitiveOutput(PrimitiveOutputKind.ControllerButton, roleButton));
            return;
        }

        result.Add(new PrimitiveOutput(PrimitiveOutputKind.Key, token));
    }

    private static void AddDirection(int numpad, List<PrimitiveOutput> result)
    {
        // Emitted even for neutral (5), which has no dpad buttons of its own, so a combo can still
        // force the d-pad to neutral instead of the physical direction bleeding through underneath it.
        result.Add(new PrimitiveOutput(PrimitiveOutputKind.DirectionOverride, string.Empty));
        foreach (var button in DirectionButtons(numpad))
        {
            result.Add(new PrimitiveOutput(PrimitiveOutputKind.ControllerButton, button));
        }
    }

    public static IEnumerable<string> DirectionButtons(int numpad) => numpad switch
    {
        7 => new[] { "dpad_up", "dpad_left" },
        8 => new[] { "dpad_up" },
        9 => new[] { "dpad_up", "dpad_right" },
        4 => new[] { "dpad_left" },
        6 => new[] { "dpad_right" },
        1 => new[] { "dpad_down", "dpad_left" },
        2 => new[] { "dpad_down" },
        3 => new[] { "dpad_down", "dpad_right" },
        _ => Array.Empty<string>(),
    };
}
