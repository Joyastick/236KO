using MotionInput.Core.Models;

namespace MotionInput.Core.Input;

/// <summary>
/// Detects what changed between two consecutive <see cref="ControllerSnapshot"/> polls, for a
/// "press a button to bind it" UI flow. Digital ids from both <see cref="XInputControllerSource"/>
/// and <see cref="DirectInputControllerSource"/> already match the names <see
/// cref="Output.VirtualGamepad"/> expects (a, b, x, y, lb, rb, lt, rt, start, back, ls, rs,
/// dpad_up/down/left/right), so a captured id can be used directly as a "controller:&lt;id&gt;"
/// output token for pure passthrough.
/// </summary>
public static class InputCapture
{
    private const double DirectionThreshold = 0.5;

    /// <summary>The first digital id held in <paramref name="current"/> but not <paramref name="previous"/>, if any.</summary>
    public static string? DetectNewButtonPress(ControllerSnapshot previous, ControllerSnapshot current) =>
        current.Digital.FirstOrDefault(id => !previous.Digital.Contains(id));

    /// <summary>
    /// Which direction source (dpad/left_stick/right_stick) newly reports the given cardinal
    /// direction ("left"/"right"/"up"/"down"), if any, between two polls.
    /// </summary>
    public static string? DetectDirectionSource(ControllerSnapshot previous, ControllerSnapshot current, string direction) => direction switch
    {
        "left" => NewlyHeld(previous, current, "dpad_left") ? "dpad"
            : CrossedNegative(previous, current, "leftstick_x") ? "left_stick"
            : CrossedNegative(previous, current, "rightstick_x") ? "right_stick"
            : null,
        "right" => NewlyHeld(previous, current, "dpad_right") ? "dpad"
            : CrossedPositive(previous, current, "leftstick_x") ? "left_stick"
            : CrossedPositive(previous, current, "rightstick_x") ? "right_stick"
            : null,
        "up" => NewlyHeld(previous, current, "dpad_up") ? "dpad"
            : CrossedPositive(previous, current, "leftstick_y") ? "left_stick"
            : CrossedPositive(previous, current, "rightstick_y") ? "right_stick"
            : null,
        "down" => NewlyHeld(previous, current, "dpad_down") ? "dpad"
            : CrossedNegative(previous, current, "leftstick_y") ? "left_stick"
            : CrossedNegative(previous, current, "rightstick_y") ? "right_stick"
            : null,
        _ => null,
    };

    private static bool NewlyHeld(ControllerSnapshot previous, ControllerSnapshot current, string id) =>
        !previous.IsHeld(id) && current.IsHeld(id);

    private static bool CrossedPositive(ControllerSnapshot previous, ControllerSnapshot current, string id) =>
        previous.AnalogValue(id) < DirectionThreshold && current.AnalogValue(id) >= DirectionThreshold;

    private static bool CrossedNegative(ControllerSnapshot previous, ControllerSnapshot current, string id) =>
        previous.AnalogValue(id) > -DirectionThreshold && current.AnalogValue(id) <= -DirectionThreshold;
}
