namespace MotionInput.Core.Input;

/// <summary>
/// A candidate game-controller PnP device node and which driver currently owns it. See
/// <see cref="ControllerDriverInspector"/> for how "candidate" is decided and its caveats.
/// </summary>
public sealed record ControllerDriverInfo(string InstanceId, string FriendlyName, string? DriverService, string? DeviceClass)
{
    /// <summary>
    /// Best-effort guess at whether this device is currently bound to Windows' Xbox-compatible
    /// class driver (commonly service name "XUSB22"/"XUSB21" depending on Windows version) and
    /// therefore visible to XInput/Windows.Gaming.Input — as opposed to the generic HID driver
    /// ("HidUsb"), which is invisible to those APIs but still readable via DirectInput/raw HID.
    /// </summary>
    public bool IsBoundToXInputDriver =>
        DriverService is not null && DriverService.Contains("xusb", StringComparison.OrdinalIgnoreCase);

    /// <summary>Friendly status string for display, based on <see cref="IsBoundToXInputDriver"/>.</summary>
    public string StatusText => IsBoundToXInputDriver ? "XInput-visible" : "Hidden from XInput";
}
