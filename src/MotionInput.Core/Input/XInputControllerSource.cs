using MotionInput.Core.Models;
using SharpDX.XInput;

namespace MotionInput.Core.Input;

/// <summary>Reads an Xbox-compatible pad through XInput. Lowest latency, standard button layout.</summary>
public sealed class XInputControllerSource : IControllerSource
{
    private const float ButtonTriggerThreshold = 0.5f;

    private readonly Controller _controller;

    public XInputControllerSource(ControllerDescriptor descriptor, UserIndex userIndex)
    {
        Descriptor = descriptor;
        _controller = new Controller(userIndex);
    }

    public ControllerDescriptor Descriptor { get; }

    public bool IsConnected => _controller.IsConnected;

    public ControllerSnapshot Poll()
    {
        if (!_controller.IsConnected)
        {
            return ControllerSnapshot.Empty;
        }

        var state = _controller.GetState();
        var pad = state.Gamepad;
        var buttons = pad.Buttons;

        var digital = new HashSet<string>();
        void AddIf(GamepadButtonFlags flag, string id)
        {
            if ((buttons & flag) != 0)
            {
                digital.Add(id);
            }
        }

        AddIf(GamepadButtonFlags.DPadUp, "dpad_up");
        AddIf(GamepadButtonFlags.DPadDown, "dpad_down");
        AddIf(GamepadButtonFlags.DPadLeft, "dpad_left");
        AddIf(GamepadButtonFlags.DPadRight, "dpad_right");
        AddIf(GamepadButtonFlags.A, "a");
        AddIf(GamepadButtonFlags.B, "b");
        AddIf(GamepadButtonFlags.X, "x");
        AddIf(GamepadButtonFlags.Y, "y");
        AddIf(GamepadButtonFlags.LeftShoulder, "lb");
        AddIf(GamepadButtonFlags.RightShoulder, "rb");
        AddIf(GamepadButtonFlags.Start, "start");
        AddIf(GamepadButtonFlags.Back, "back");
        AddIf(GamepadButtonFlags.LeftThumb, "ls");
        AddIf(GamepadButtonFlags.RightThumb, "rs");

        var leftTrigger = pad.LeftTrigger / 255f;
        var rightTrigger = pad.RightTrigger / 255f;
        if (leftTrigger >= ButtonTriggerThreshold) digital.Add("lt");
        if (rightTrigger >= ButtonTriggerThreshold) digital.Add("rt");

        var analog = new Dictionary<string, float>
        {
            ["leftstick_x"] = Normalize(pad.LeftThumbX),
            ["leftstick_y"] = Normalize(pad.LeftThumbY),
            ["rightstick_x"] = Normalize(pad.RightThumbX),
            ["rightstick_y"] = Normalize(pad.RightThumbY),
            ["lefttrigger"] = leftTrigger,
            ["righttrigger"] = rightTrigger,
        };

        return new ControllerSnapshot(DateTime.UtcNow, digital, analog);
    }

    private static float Normalize(short raw) => Math.Clamp(raw / 32767f, -1f, 1f);

    public void Dispose()
    {
        // Controller is a lightweight struct-backed handle; nothing to release.
    }
}
