using SharpDX.XInput;
using SharpDX.DirectInput;

namespace MotionInput.Core.Input;

/// <summary>Enumerates connected controllers across both backends and builds a live source for the chosen one.</summary>
public sealed class ControllerManager
{
    private readonly DirectInput _directInput = new();

    public IReadOnlyList<ControllerDescriptor> ListAvailable()
    {
        var result = new List<ControllerDescriptor>();

        foreach (UserIndex index in Enum.GetValues<UserIndex>())
        {
            if (index == UserIndex.Any) continue;
            var controller = new Controller(index);
            if (controller.IsConnected)
            {
                result.Add(new ControllerDescriptor($"xinput:{(int)index}", $"Controller {(int)index + 1} (XInput)", ControllerBackend.XInput));
            }
        }

        foreach (var device in _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
        {
            result.Add(new ControllerDescriptor($"directinput:{device.InstanceGuid}", $"{device.InstanceName} (DirectInput)", ControllerBackend.DirectInput));
        }

        return result;
    }

    public IControllerSource Create(ControllerDescriptor descriptor, IntPtr windowHandle)
    {
        return descriptor.Backend switch
        {
            ControllerBackend.XInput => new XInputControllerSource(descriptor, (UserIndex)int.Parse(descriptor.Id.Split(':')[1])),
            ControllerBackend.DirectInput => new DirectInputControllerSource(
                descriptor,
                _directInput,
                Guid.Parse(descriptor.Id.Split(':')[1]),
                windowHandle),
            _ => throw new NotSupportedException($"Unknown backend: {descriptor.Backend}"),
        };
    }

    public IControllerSource? CreateFirstAvailable(IntPtr windowHandle)
    {
        var first = ListAvailable().FirstOrDefault();
        return first is null ? null : Create(first, windowHandle);
    }
}
