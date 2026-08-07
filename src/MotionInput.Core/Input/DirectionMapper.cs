using MotionInput.Core.Models;

namespace MotionInput.Core.Input;

/// <summary>
/// Reduces a raw controller snapshot down to a single numpad direction (1-9, 5 = neutral),
/// combining whichever sources (dpad / left stick / right stick) the profile has enabled.
/// Opposite directions on the same axis cancel out (SOCD neutral), matching how most fighting
/// games resolve simultaneous left+right or up+down.
/// </summary>
public static class DirectionMapper
{
    public const int Neutral = 5;

    public static int Map(ControllerSnapshot snapshot, ControllerInputSettings settings)
    {
        bool up = false, down = false, left = false, right = false;

        foreach (var source in settings.DirectionSources)
        {
            switch (source)
            {
                case "dpad":
                    up |= snapshot.IsHeld("dpad_up");
                    down |= snapshot.IsHeld("dpad_down");
                    left |= snapshot.IsHeld("dpad_left");
                    right |= snapshot.IsHeld("dpad_right");
                    break;
                case "left_stick":
                    ApplyStick(snapshot, "leftstick_x", "leftstick_y", settings.StickDeadzone, ref up, ref down, ref left, ref right);
                    break;
                case "right_stick":
                    ApplyStick(snapshot, "rightstick_x", "rightstick_y", settings.StickDeadzone, ref up, ref down, ref left, ref right);
                    break;
            }
        }

        // SOCD cancellation: opposing directions on the same axis neutralize each other.
        if (up && down) { up = false; down = false; }
        if (left && right) { left = false; right = false; }

        return ToNumpad(up, down, left, right);
    }

    private static void ApplyStick(ControllerSnapshot snapshot, string xId, string yId, double deadzone, ref bool up, ref bool down, ref bool left, ref bool right)
    {
        var x = snapshot.AnalogValue(xId);
        var y = snapshot.AnalogValue(yId);
        if (x >= deadzone) right = true;
        if (x <= -deadzone) left = true;
        if (y >= deadzone) up = true;
        if (y <= -deadzone) down = true;
    }

    private static int ToNumpad(bool up, bool down, bool left, bool right)
    {
        if (up && left) return 7;
        if (up && right) return 9;
        if (up) return 8;
        if (down && left) return 1;
        if (down && right) return 3;
        if (down) return 2;
        if (left) return 4;
        if (right) return 6;
        return Neutral;
    }

    /// <summary>True if <paramref name="candidate"/> is numpad-adjacent to <paramref name="required"/> (shares a compass component), used for diagonal-skip leniency.</summary>
    public static bool IsAdjacent(int candidate, int required)
    {
        if (candidate == required) return true;
        if (candidate == Neutral || required == Neutral) return false;

        var (cx, cy) = ToAxes(candidate);
        var (rx, ry) = ToAxes(required);

        // Ring-adjacent (one compass "click" away, e.g. 2->3 or 2->1) means Manhattan distance 1.
        // Chebyshev distance would also call perpendicular cardinals like 2->6 "adjacent", which
        // they aren't on the compass (3 sits between them), so Manhattan is the correct metric here.
        var dx = Math.Abs(cx - rx);
        var dy = Math.Abs(cy - ry);
        return dx + dy == 1;
    }

    private static (int x, int y) ToAxes(int numpad) => numpad switch
    {
        7 => (-1, 1),
        8 => (0, 1),
        9 => (1, 1),
        4 => (-1, 0),
        5 => (0, 0),
        6 => (1, 0),
        1 => (-1, -1),
        2 => (0, -1),
        3 => (1, -1),
        _ => (0, 0),
    };
}
