namespace MotionInput.Core.Output;

/// <summary>An emulated Xbox 360 pad that the target game reads instead of (or alongside) the real controller.</summary>
public interface IVirtualGamepad : IDisposable
{
    bool IsConnected { get; }

    void Connect();

    /// <summary>Set one button/dpad direction/trigger on or off. Name is one of the primitive controller button values (see <see cref="OutputResolver"/>).</summary>
    void SetButton(string name, bool pressed);

    void ResetAll();
}
