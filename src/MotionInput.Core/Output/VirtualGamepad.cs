using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace MotionInput.Core.Output;

/// <summary>Emulated Xbox 360 controller backed by ViGEmBus. Requires the ViGEm bus driver to be installed.</summary>
public sealed class VirtualGamepad : IVirtualGamepad
{
    private readonly ViGEmClient _client;
    private readonly IXbox360Controller _pad;
    private bool _connected;

    public VirtualGamepad()
    {
        _client = new ViGEmClient();
        _pad = _client.CreateXbox360Controller();
    }

    public bool IsConnected => _connected;

    public void Connect()
    {
        if (_connected) return;
        _pad.Connect();
        _connected = true;
    }

    public void SetButton(string name, bool pressed)
    {
        if (!_connected) return;

        switch (name)
        {
            case "lt":
                _pad.SetSliderValue(Xbox360Slider.LeftTrigger, pressed ? (byte)255 : (byte)0);
                return;
            case "rt":
                _pad.SetSliderValue(Xbox360Slider.RightTrigger, pressed ? (byte)255 : (byte)0);
                return;
        }

        var button = MapButton(name);
        if (button is not null)
        {
            _pad.SetButtonState(button, pressed);
        }
    }

    public void ResetAll()
    {
        if (!_connected) return;

        foreach (var button in AllButtons)
        {
            _pad.SetButtonState(button, false);
        }
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, 0);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, 0);
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, 0);
    }

    private static readonly Xbox360Button[] AllButtons =
    {
        Xbox360Button.A, Xbox360Button.B, Xbox360Button.X, Xbox360Button.Y,
        Xbox360Button.LeftShoulder, Xbox360Button.RightShoulder,
        Xbox360Button.Start, Xbox360Button.Back,
        Xbox360Button.LeftThumb, Xbox360Button.RightThumb, Xbox360Button.Guide,
        Xbox360Button.Up, Xbox360Button.Down, Xbox360Button.Left, Xbox360Button.Right,
    };

    private static Xbox360Button? MapButton(string name) => name switch
    {
        "a" => Xbox360Button.A,
        "b" => Xbox360Button.B,
        "x" => Xbox360Button.X,
        "y" => Xbox360Button.Y,
        "lb" => Xbox360Button.LeftShoulder,
        "rb" => Xbox360Button.RightShoulder,
        "start" => Xbox360Button.Start,
        "back" => Xbox360Button.Back,
        "ls" => Xbox360Button.LeftThumb,
        "rs" => Xbox360Button.RightThumb,
        "guide" => Xbox360Button.Guide,
        "dpad_up" => Xbox360Button.Up,
        "dpad_down" => Xbox360Button.Down,
        "dpad_left" => Xbox360Button.Left,
        "dpad_right" => Xbox360Button.Right,
        _ => null,
    };

    public void Dispose()
    {
        if (_connected)
        {
            try { _pad.Disconnect(); } catch { /* already gone */ }
        }
        _client.Dispose();
    }
}
