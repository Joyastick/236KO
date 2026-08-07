using MotionInput.Core.Models;

namespace MotionInput.Core.Input;

/// <summary>A live connection to one physical controller, polled once per engine tick.</summary>
public interface IControllerSource : IDisposable
{
    ControllerDescriptor Descriptor { get; }

    bool IsConnected { get; }

    /// <summary>Read the controller's current state. Should be cheap enough to call at a few hundred Hz.</summary>
    ControllerSnapshot Poll();
}
