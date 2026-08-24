using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MotionInput.Core.Input;

/// <summary>
/// Read-only inspection of which driver a connected game-controller-like device is currently
/// bound to, a way to launch Windows' own "Update Driver" dialog for one, and — for those willing
/// to skip the manual wizard — a programmatic rebind via <see cref="TryRebindToGenericHid"/>/
/// <see cref="TryRebindToXInputDriver"/>. This is how a real XInput pad gets hidden from a game
/// without HIDHide, which can't touch XInput at all (see the Bindings tab's design notes).
///
/// Caveats (this is inherently best-effort — Windows doesn't expose one single documented way to
/// ask "is this specifically the Xbox-compatible class driver"):
///   - Detection matches on hardware/compatible IDs containing "IG_" (the interface-descriptor
///     marker Microsoft's XInput convention uses on composite Xbox-style controllers) or a
///     friendly name/description containing "xbox"/"xinput", OR a driver service name containing
///     "xusb". Different controllers, OS versions, and third-party drivers may not match exactly.
///   - The "open driver properties" call uses devmgr.dll's DeviceProperties_RunDLL entry point,
///     the same one Device Manager's own "Open" action and many long-standing admin scripts use.
///     It is not officially documented by Microsoft, but has been stable since Windows XP.
///   - The rebind methods use the exact same SetupDiBuildDriverInfoList/SetupDiSetSelectedDriver/
///     SetupDiCallClassInstaller(DIF_INSTALLDEVICE) sequence Device Manager's own "Update Driver →
///     pick from list" wizard uses internally — but which compatible drivers a given device offers
///     (and whether "HID-compliant game controller" is among them) varies by hardware and Windows
///     version, so success isn't guaranteed. Requires Administrator privileges; callers should
///     re-launch themselves elevated (verb "runas") to invoke these two methods specifically,
///     rather than requiring the whole app to run elevated at all times.
/// </summary>
public static class ControllerDriverInspector
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;

    private const int SpdrpDeviceDesc = 0x00000000;
    private const int SpdrpHardwareId = 0x00000001;
    private const int SpdrpCompatibleIds = 0x00000002;
    private const int SpdrpService = 0x00000004;
    private const int SpdrpClass = 0x00000007;
    private const int SpdrpFriendlyName = 0x0000000C;

    private const int CrSuccess = 0;

    private const uint SpditCompatDriver = 0x00000002; // SPDIT_COMPATDRIVER
    private const uint DifInstallDevice = 0x00000002; // DIF_INSTALLDEVICE

    /// <summary>Lists present devices that look like they could be an XInput-style game controller, with whatever driver currently owns each.</summary>
    public static IReadOnlyList<ControllerDriverInfo> ListCandidates()
    {
        var results = new List<ControllerDriverInfo>();

        var deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet.ToInt64() == -1)
        {
            return results;
        }

        try
        {
            var devInfoData = new SP_DEVINFO_DATA();
            devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

            for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref devInfoData); index++)
            {
                var hardwareIds = GetMultiStringProperty(deviceInfoSet, ref devInfoData, SpdrpHardwareId);
                var compatibleIds = GetMultiStringProperty(deviceInfoSet, ref devInfoData, SpdrpCompatibleIds);
                var friendlyName = GetStringProperty(deviceInfoSet, ref devInfoData, SpdrpFriendlyName)
                                   ?? GetStringProperty(deviceInfoSet, ref devInfoData, SpdrpDeviceDesc)
                                   ?? "(unnamed device)";
                var service = GetStringProperty(deviceInfoSet, ref devInfoData, SpdrpService);
                var deviceClass = GetStringProperty(deviceInfoSet, ref devInfoData, SpdrpClass);

                var looksLikeXInputController =
                    hardwareIds.Concat(compatibleIds).Any(id => id.Contains("IG_", StringComparison.OrdinalIgnoreCase)) ||
                    friendlyName.Contains("xbox", StringComparison.OrdinalIgnoreCase) ||
                    friendlyName.Contains("xinput", StringComparison.OrdinalIgnoreCase) ||
                    (service?.Contains("xusb", StringComparison.OrdinalIgnoreCase) ?? false);

                if (!looksLikeXInputController)
                {
                    continue;
                }

                var instanceId = GetInstanceId(devInfoData.DevInst);
                if (instanceId is null)
                {
                    continue;
                }

                results.Add(new ControllerDriverInfo(instanceId, friendlyName, service, deviceClass));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    /// <summary>Debug/diagnostic helper: lists every present PnP device with no filtering, for figuring out why a controller didn't match <see cref="ListCandidates"/>'s heuristic.</summary>
    public static IReadOnlyList<ControllerDriverInfo> ListAllForDebug()
    {
        var results = new List<ControllerDriverInfo>();

        var deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet.ToInt64() == -1)
        {
            return results;
        }

        try
        {
            var devInfoData = new SP_DEVINFO_DATA();
            devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

            for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref devInfoData); index++)
            {
                var friendlyName = GetStringProperty(deviceInfoSet, ref devInfoData, SpdrpFriendlyName)
                                   ?? GetStringProperty(deviceInfoSet, ref devInfoData, SpdrpDeviceDesc)
                                   ?? "(unnamed device)";
                var service = GetStringProperty(deviceInfoSet, ref devInfoData, SpdrpService);
                var deviceClass = GetStringProperty(deviceInfoSet, ref devInfoData, SpdrpClass);
                var instanceId = GetInstanceId(devInfoData.DevInst) ?? "(no instance id)";

                results.Add(new ControllerDriverInfo(instanceId, friendlyName, service, deviceClass));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    /// <summary>
    /// Opens Windows' own device properties dialog for this device (Driver tab first click away),
    /// so the user can pick "Update Driver" → "Browse my computer for drivers" → "Let me pick from
    /// a list" → "HID-compliant game controller" themselves — the interactive path is what actually
    /// knows how to do this correctly on their exact hardware/Windows version, and comes with a
    /// built-in "Roll Back Driver" undo if it doesn't work out.
    /// </summary>
    public static bool TryOpenDeviceProperties(string instanceId)
    {
        try
        {
            var args = $"devmgr.dll DeviceProperties_RunDLL /DeviceID \"{instanceId}\" /WType 0";
            using var process = Process.Start(new ProcessStartInfo("rundll32.exe", args) { UseShellExecute = true });
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Rebinds the device away from the Xbox-compatible driver to the generic HID driver, hiding it from XInput. Requires Administrator; call from an elevated process.</summary>
    public static bool TryRebindToGenericHid(string instanceId, out string? error) =>
        TryRebindDriver(instanceId, new Func<CompatibleDriverInfo, bool>[]
        {
            d => d.Description.Contains("HID-compliant game controller", StringComparison.OrdinalIgnoreCase),
            d => d.Description.Contains("HID", StringComparison.OrdinalIgnoreCase)
                 && !d.Description.Contains("xbox", StringComparison.OrdinalIgnoreCase)
                 && !d.Description.Contains("xinput", StringComparison.OrdinalIgnoreCase),
            d => !d.Description.Contains("xbox", StringComparison.OrdinalIgnoreCase)
                 && !d.Description.Contains("xinput", StringComparison.OrdinalIgnoreCase),
        }, out error);

    /// <summary>Rebinds the device back to the Xbox-compatible driver, restoring XInput visibility. Requires Administrator; call from an elevated process.</summary>
    public static bool TryRebindToXInputDriver(string instanceId, out string? error) =>
        TryRebindDriver(instanceId, new Func<CompatibleDriverInfo, bool>[]
        {
            d => d.Description.Contains("xbox", StringComparison.OrdinalIgnoreCase) || d.Description.Contains("xinput", StringComparison.OrdinalIgnoreCase),
        }, out error);

    /// <summary>Debug/diagnostic helper: lists every compatible driver Windows offers for this device (the same list "Update Driver → Let me pick from a list" shows).</summary>
    public static IReadOnlyList<CompatibleDriverInfo> ListCompatibleDrivers(string instanceId) =>
        ListCompatibleDrivers(instanceId, out _);

    /// <summary>Same as <see cref="ListCompatibleDrivers(string)"/> but also reports which stage produced an empty result, since an empty list is otherwise ambiguous (device not found vs. API failure vs. genuinely zero compatible drivers).</summary>
    public static IReadOnlyList<CompatibleDriverInfo> ListCompatibleDrivers(string instanceId, out string? diagnostic)
    {
        var results = new List<CompatibleDriverInfo>();

        var deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet.ToInt64() == -1)
        {
            diagnostic = $"SetupDiGetClassDevs failed (Win32 error {Marshal.GetLastWin32Error()}).";
            return results;
        }

        try
        {
            if (!TryFindDeviceInfoData(deviceInfoSet, instanceId, out var devInfoData))
            {
                diagnostic = $"Device {instanceId} was not found among present devices.";
                return results;
            }

            if (!SetupDiBuildDriverInfoList(deviceInfoSet, ref devInfoData, SpditCompatDriver))
            {
                diagnostic = $"SetupDiBuildDriverInfoList failed (Win32 error {Marshal.GetLastWin32Error()}).";
                return results;
            }

            diagnostic = null;
            try
            {
                var drvInfoData = new SP_DRVINFO_DATA_W();
                drvInfoData.cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_W>();

                for (uint index = 0; SetupDiEnumDriverInfo(deviceInfoSet, ref devInfoData, SpditCompatDriver, index, ref drvInfoData); index++)
                {
                    results.Add(new CompatibleDriverInfo(drvInfoData.Description, drvInfoData.MfgName, drvInfoData.ProviderName));
                    drvInfoData.cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_W>();
                }
            }
            finally
            {
                SetupDiDestroyDriverInfoList(deviceInfoSet, ref devInfoData, SpditCompatDriver);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    private static bool TryRebindDriver(string instanceId, IReadOnlyList<Func<CompatibleDriverInfo, bool>> predicatesInPriorityOrder, out string? error)
    {
        var deviceInfoSet = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet.ToInt64() == -1)
        {
            error = "Could not enumerate devices (SetupDiGetClassDevs failed).";
            return false;
        }

        try
        {
            if (!TryFindDeviceInfoData(deviceInfoSet, instanceId, out var devInfoData))
            {
                error = $"Device {instanceId} is not currently present.";
                return false;
            }

            if (!SetupDiBuildDriverInfoList(deviceInfoSet, ref devInfoData, SpditCompatDriver))
            {
                error = $"Could not list compatible drivers (Win32 error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            try
            {
                var drvInfoData = new SP_DRVINFO_DATA_W();
                drvInfoData.cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_W>();

                foreach (var predicate in predicatesInPriorityOrder)
                {
                    var candidate = new SP_DRVINFO_DATA_W();
                    var found = false;

                    for (uint index = 0; SetupDiEnumDriverInfo(deviceInfoSet, ref devInfoData, SpditCompatDriver, index, ref drvInfoData); index++)
                    {
                        if (predicate(new CompatibleDriverInfo(drvInfoData.Description, drvInfoData.MfgName, drvInfoData.ProviderName)))
                        {
                            candidate = drvInfoData;
                            found = true;
                            break;
                        }
                        drvInfoData.cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_W>();
                    }

                    if (!found)
                    {
                        continue;
                    }

                    if (!SetupDiSetSelectedDriver(deviceInfoSet, ref devInfoData, ref candidate))
                    {
                        error = $"Could not select driver \"{candidate.Description}\" (Win32 error {Marshal.GetLastWin32Error()}).";
                        return false;
                    }

                    if (!SetupDiCallClassInstaller(DifInstallDevice, deviceInfoSet, ref devInfoData))
                    {
                        error = $"Windows refused to install driver \"{candidate.Description}\" (Win32 error {Marshal.GetLastWin32Error()}). This usually means Administrator privileges are required.";
                        return false;
                    }

                    error = null;
                    return true;
                }

                error = "No compatible driver on this device matched what we were looking for. Try \"Update driver…\" and pick manually instead.";
                return false;
            }
            finally
            {
                SetupDiDestroyDriverInfoList(deviceInfoSet, ref devInfoData, SpditCompatDriver);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static bool TryFindDeviceInfoData(IntPtr deviceInfoSet, string instanceId, out SP_DEVINFO_DATA devInfoData)
    {
        devInfoData = new SP_DEVINFO_DATA();
        devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

        for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref devInfoData); index++)
        {
            if (string.Equals(GetInstanceId(devInfoData.DevInst), instanceId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();
        }

        return false;
    }

    private static string? GetInstanceId(uint devInst)
    {
        var buffer = new StringBuilder(512);
        var result = CM_Get_Device_ID(devInst, buffer, buffer.Capacity, 0);
        return result == CrSuccess ? buffer.ToString() : null;
    }

    private static string? GetStringProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfoData, int property)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref devInfoData, property, out _, buffer, buffer.Length, out var requiredSize) || requiredSize == 0)
        {
            return null;
        }
        var text = Encoding.Unicode.GetString(buffer, 0, (int)requiredSize).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static IReadOnlyList<string> GetMultiStringProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfoData, int property)
    {
        var buffer = new byte[2048];
        if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref devInfoData, property, out _, buffer, buffer.Length, out var requiredSize) || requiredSize == 0)
        {
            return Array.Empty<string>();
        }

        var text = Encoding.Unicode.GetString(buffer, 0, (int)requiredSize);
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SP_DRVINFO_DATA_W
    {
        public uint cbSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string MfgName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProviderName;
        public long DriverDate;
        public ulong DriverVersion;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, int property, out uint propertyRegDataType, byte[] propertyBuffer, int propertyBufferSize, out uint requiredSize);

    [DllImport("cfgmgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID(uint devInst, StringBuilder buffer, int bufferLen, int flags);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiBuildDriverInfoList(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint driverType);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiEnumDriverInfo(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint driverType, uint memberIndex, ref SP_DRVINFO_DATA_W driverInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiSetSelectedDriver(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref SP_DRVINFO_DATA_W driverInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDriverInfoList(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint driverType);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);
}

/// <summary>One entry from Windows' "compatible drivers" list for a device (what "Update Driver → Let me pick from a list" shows).</summary>
public sealed record CompatibleDriverInfo(string Description, string MfgName, string ProviderName);
