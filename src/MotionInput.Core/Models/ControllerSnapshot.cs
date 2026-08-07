namespace MotionInput.Core.Models;

/// <summary>
/// A single poll of a controller's raw state. Digital ids are backend-specific strings
/// (e.g. "a", "dpad_up", "button3", "pov0_up") so XInput and DirectInput devices can share
/// the same binding/direction-mapping pipeline. Analog ids are similarly backend-specific
/// (e.g. "leftstick_x", "lefttrigger") and range -1..1 for sticks, 0..1 for triggers.
/// </summary>
public sealed class ControllerSnapshot
{
    public static readonly ControllerSnapshot Empty = new(DateTime.UtcNow, new HashSet<string>(), new Dictionary<string, float>());

    public ControllerSnapshot(DateTime timestamp, IReadOnlySet<string> digital, IReadOnlyDictionary<string, float> analog)
    {
        Timestamp = timestamp;
        Digital = digital;
        Analog = analog;
    }

    public DateTime Timestamp { get; }

    public IReadOnlySet<string> Digital { get; }

    public IReadOnlyDictionary<string, float> Analog { get; }

    public bool IsHeld(string id) => Digital.Contains(id);

    public float AnalogValue(string id) => Analog.TryGetValue(id, out var v) ? v : 0f;
}
