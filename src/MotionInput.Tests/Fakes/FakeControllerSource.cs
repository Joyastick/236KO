using MotionInput.Core.Input;
using MotionInput.Core.Models;

namespace MotionInput.Tests.Fakes;

/// <summary>Test double whose held digital inputs can be changed on the fly by the test.</summary>
public sealed class FakeControllerSource : IControllerSource
{
    private readonly object _gate = new();
    private HashSet<string> _held = new();

    public FakeControllerSource()
    {
        Descriptor = new ControllerDescriptor("fake:0", "Fake", ControllerBackend.XInput);
    }

    public ControllerDescriptor Descriptor { get; }

    public bool IsConnected => true;

    public void SetHeld(params string[] ids)
    {
        lock (_gate)
        {
            _held = ids.ToHashSet();
        }
    }

    public ControllerSnapshot Poll()
    {
        lock (_gate)
        {
            return new ControllerSnapshot(DateTime.UtcNow, new HashSet<string>(_held), new Dictionary<string, float>());
        }
    }

    public void Dispose()
    {
    }
}
