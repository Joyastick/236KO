using System.Collections.Concurrent;
using MotionInput.Core.Output;

namespace MotionInput.Tests.Fakes;

/// <summary>Test double that records every SetButton call and the currently-held set, without any real ViGEmBus device.</summary>
public sealed class FakeVirtualGamepad : IVirtualGamepad
{
    public ConcurrentQueue<(string Name, bool Pressed)> Calls { get; } = new();
    private readonly HashSet<string> _held = new();
    private readonly object _gate = new();

    public bool IsConnected { get; private set; }

    public void Connect() => IsConnected = true;

    public void SetButton(string name, bool pressed)
    {
        Calls.Enqueue((name, pressed));
        lock (_gate)
        {
            if (pressed) _held.Add(name);
            else _held.Remove(name);
        }
    }

    public bool IsHeld(string name)
    {
        lock (_gate)
        {
            return _held.Contains(name);
        }
    }

    public void ResetAll()
    {
        lock (_gate)
        {
            _held.Clear();
        }
    }

    public void Dispose()
    {
    }
}
