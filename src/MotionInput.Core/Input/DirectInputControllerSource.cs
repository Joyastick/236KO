using MotionInput.Core.Models;
using SharpDX.DirectInput;

namespace MotionInput.Core.Input;

/// <summary>
/// Reads a non-XInput ("legacy"/generic HID) pad through DirectInput. Buttons are exposed as
/// generic "button0".."buttonN" ids and the primary hat switch as dpad_* ids, so bindings work
/// the same way as they do for an XInput pad even though the physical layout is unknown.
/// </summary>
public sealed class DirectInputControllerSource : IControllerSource
{
    private readonly Joystick _joystick;

    public DirectInputControllerSource(ControllerDescriptor descriptor, DirectInput directInput, Guid instanceGuid, IntPtr windowHandle)
    {
        Descriptor = descriptor;
        _joystick = new Joystick(directInput, instanceGuid);
        _joystick.Properties.BufferSize = 128;
        _joystick.SetCooperativeLevel(windowHandle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
        _joystick.Acquire();
    }

    public ControllerDescriptor Descriptor { get; }

    public bool IsConnected
    {
        get
        {
            try
            {
                _joystick.Poll();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public ControllerSnapshot Poll()
    {
        try
        {
            _joystick.Poll();
        }
        catch
        {
            return ControllerSnapshot.Empty;
        }

        JoystickState state;
        try
        {
            state = _joystick.GetCurrentState();
        }
        catch
        {
            return ControllerSnapshot.Empty;
        }

        var digital = new HashSet<string>();
        for (var i = 0; i < state.Buttons.Length; i++)
        {
            if (state.Buttons[i])
            {
                digital.Add($"button{i}");
            }
        }

        // Some boards (notably PS3/PS4-compatible arcade-stick encoders in DirectInput mode) put the
        // d-pad on a hat switch that isn't necessarily index 0, or a browser/tester tool reports it as
        // "axis 9" purely because that's how such tools flatten a hat's angle into the axis list — the
        // underlying DirectInput object is still a point-of-view controller either way, so scan all of
        // them rather than assuming index 0 is the live one.
        for (var i = 0; i < state.PointOfViewControllers.Length; i++)
        {
            var pov = state.PointOfViewControllers[i];
            if (pov < 0) continue;

            foreach (var dpadId in HatAngleToDpadIds(pov))
            {
                digital.Add(dpadId);
            }
        }

        var analog = new Dictionary<string, float>
        {
            ["leftstick_x"] = Normalize(state.X),
            ["leftstick_y"] = -Normalize(state.Y),
            ["rightstick_x"] = Normalize(state.RotationZ),
            ["rightstick_y"] = -Normalize(state.Z),
            // Raw copies of every axis DirectInput exposes (independent of which ones feed the stick
            // aliases above), plus the hat angles, so the Monitor tab can show what a given physical
            // input reports even before it's bound to anything. This is what makes it possible to
            // figure out, e.g., that a tester tool's "axis 9" is really point-of-view controller 0.
            ["axis_x"] = Normalize(state.X),
            ["axis_y"] = Normalize(state.Y),
            ["axis_z"] = Normalize(state.Z),
            ["axis_rx"] = Normalize(state.RotationX),
            ["axis_ry"] = Normalize(state.RotationY),
            ["axis_rz"] = Normalize(state.RotationZ),
        };

        for (var i = 0; i < state.Sliders.Length; i++)
        {
            analog[$"axis_slider{i}"] = Normalize(state.Sliders[i]);
        }

        for (var i = 0; i < state.PointOfViewControllers.Length; i++)
        {
            var pov = state.PointOfViewControllers[i];
            analog[$"pov{i}"] = pov < 0 ? -1f : pov / 36000f;
        }

        return new ControllerSnapshot(DateTime.UtcNow, digital, analog);
    }

    /// <summary>
    /// Maps a DirectInput hat-switch angle (hundredths of a degree, clockwise from up, or negative
    /// for centered/no reading) to the dpad_* id(s) it represents. Each cardinal's bucket is +/-45
    /// degrees around it, with inclusive bounds on both ends, so a hat's exact diagonal positions
    /// (4500, 13500, 22500, 31500 — what an 8-way hat switch actually reports, not a continuous
    /// range) land in both neighboring buckets and raise both axes, matching a d-pad's diagonal
    /// press. A previous mismatched inclusive/exclusive bound here made every diagonal position
    /// resolve to only one of its two cardinals — the motion buffer then recorded that single
    /// (frequently duplicate-with-the-next-sample) cardinal instead of the actual diagonal,
    /// silently dropping required intermediate steps and making every diagonal-involving motion
    /// (qcf, qcb, dp, rdp, half-circles — i.e. nearly all of them) fail to match, even though the
    /// live Held Inputs/Direction readout looked correct at each instant.
    /// </summary>
    public static IEnumerable<string> HatAngleToDpadIds(int pov)
    {
        if (pov < 0) yield break;

        if (pov is >= 31500 or <= 4500) yield return "dpad_up";
        if (pov is >= 4500 and <= 13500) yield return "dpad_right";
        if (pov is >= 13500 and <= 22500) yield return "dpad_down";
        if (pov is >= 22500 and <= 31500) yield return "dpad_left";
    }

    private static float Normalize(int raw) => Math.Clamp((raw - 32767) / 32767f, -1f, 1f);

    public void Dispose()
    {
        _joystick.Unacquire();
        _joystick.Dispose();
    }
}
