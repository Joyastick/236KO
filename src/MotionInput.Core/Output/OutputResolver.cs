namespace MotionInput.Core.Output;

/// <summary>
/// Turns a profile's output token strings into concrete <see cref="PrimitiveOutput"/>s.
/// Supported tokens:
///   controller:&lt;button&gt;         literal controller button/dpad press (a, b, x, y, lb, rb, lt, rt, start, back, ls, rs, dpad_up/down/left/right)
///   controller_direction:&lt;1-9&gt;   dpad press(es) for an explicit numpad direction (diagonals press two buttons)
///   $controller_motion_final       dpad press(es) for the matched motion's final direction
///   $controller_motion_start       dpad press(es) for the matched motion's starting direction
///   $attack                        the controller button the triggering attack role resolves to
///   anything else                  treated as a literal keyboard key name
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
            if (int.TryParse(token["controller_direction:".Length..], out var numpad))
            {
                foreach (var button in DirectionButtons(numpad))
                {
                    result.Add(new PrimitiveOutput(PrimitiveOutputKind.ControllerButton, button));
                }
            }
            return;
        }

        switch (token)
        {
            case "$controller_motion_final":
                if (context.FinalDirection is { } finalDir)
                {
                    foreach (var button in DirectionButtons(finalDir))
                    {
                        result.Add(new PrimitiveOutput(PrimitiveOutputKind.ControllerButton, button));
                    }
                }
                return;
            case "$controller_motion_start":
                if (context.StartDirection is { } startDir)
                {
                    foreach (var button in DirectionButtons(startDir))
                    {
                        result.Add(new PrimitiveOutput(PrimitiveOutputKind.ControllerButton, button));
                    }
                }
                return;
            case "$attack":
                if (context.AttackControllerButton is { } attackButton)
                {
                    result.Add(new PrimitiveOutput(PrimitiveOutputKind.ControllerButton, attackButton));
                }
                return;
        }

        result.Add(new PrimitiveOutput(PrimitiveOutputKind.Key, token));
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
