namespace MotionInput.Core.Models;

/// <summary>Which physical inputs feed the numpad direction mapper, and how sensitive they are.</summary>
public sealed class ControllerInputSettings
{
    /// <summary>Any of "dpad", "left_stick", "right_stick" — first source that reports a non-neutral value wins per axis, all are OR'd together.</summary>
    public List<string> DirectionSources { get; set; } = new() { "dpad", "left_stick" };

    /// <summary>0..1 fraction of stick travel required before a stick axis counts as pressed.</summary>
    public double StickDeadzone { get; set; } = 0.35;

    /// <summary>0..1 fraction of trigger travel required before a trigger counts as pressed.</summary>
    public double TriggerThreshold { get; set; } = 0.35;

    /// <summary>Id of the selected controller, e.g. "xinput:0" or "directinput:{guid}". Null = first detected.</summary>
    public string? SelectedControllerId { get; set; }

    /// <summary>Poll rate in Hz for the input loop.</summary>
    public int PollRateHz { get; set; } = 250;
}
