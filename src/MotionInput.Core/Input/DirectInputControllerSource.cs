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

        if (state.PointOfViewControllers.Length > 0)
        {
            var pov = state.PointOfViewControllers[0];
            if (pov >= 0)
            {
                // Hundredths of a degree, clockwise from up. Treat anything within 67.5 degrees of an
                // axis as that direction so diagonal hat positions raise both axes (matches a d-pad).
                if (pov is > 31500 or <= 4500) digital.Add("dpad_up");
                if (pov is > 4500 and <= 13500) digital.Add("dpad_right");
                if (pov is > 13500 and <= 22500) digital.Add("dpad_down");
                if (pov is > 22500 and <= 31500) digital.Add("dpad_left");
            }
        }

        var analog = new Dictionary<string, float>
        {
            ["leftstick_x"] = Normalize(state.X),
            ["leftstick_y"] = -Normalize(state.Y),
            ["rightstick_x"] = Normalize(state.RotationZ),
            ["rightstick_y"] = -Normalize(state.Z),
        };

        return new ControllerSnapshot(DateTime.UtcNow, digital, analog);
    }

    private static float Normalize(int raw) => Math.Clamp((raw - 32767) / 32767f, -1f, 1f);

    public void Dispose()
    {
        _joystick.Unacquire();
        _joystick.Dispose();
    }
}
