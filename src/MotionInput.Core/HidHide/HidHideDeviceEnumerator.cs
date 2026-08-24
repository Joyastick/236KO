using System.Runtime.InteropServices;
using System.Text;

namespace MotionInput.Core.HidHide;

/// <summary>
/// Lists connected HID device interfaces via SetupAPI/CfgMgr32, producing the same backslash-delimited
/// instance-id strings (e.g. "HID\VID_045E&amp;PID_028E&amp;IG_00\7&amp;1a2b3c4d&amp;0&amp;0000") that
/// <see cref="HidHideService"/> passes to the driver's blocked-instance list. This is a
/// from-scratch enumerator rather than a wrapper around HidHideCLI.exe, since shelling out to the CLI
/// and parsing its console output proved unreliable in an earlier version of this tool.
/// </summary>
public static class HidHideDeviceEnumerator
{
    private static Guid HidDeviceInterfaceGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int SpdrpDeviceDesc = 0x00000000;
    private const int SpdrpFriendlyName = 0x0000000C;
    private const int CrSuccess = 0;

    private static readonly DEVPROPKEY DevpkeyDeviceBusReportedDeviceDesc =
        new(new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), 4);
    private static readonly DEVPROPKEY DevpkeyDeviceFriendlyName =
        new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static readonly DEVPROPKEY DevpkeyDeviceDeviceDesc =
        new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);

    public static IReadOnlyList<HidHideDeviceInfo> List()
    {
        var results = new List<HidHideDeviceInfo>();

        var deviceInfoSet = SetupDiGetClassDevs(ref HidDeviceInterfaceGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet.ToInt64() == -1)
        {
            return results;
        }

        try
        {
            var interfaceData = new SP_DEVICE_INTERFACE_DATA();
            interfaceData.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

            for (uint index = 0; SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref HidDeviceInterfaceGuid, index, ref interfaceData); index++)
            {
                var devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

                SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);

                var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize must be the size of the fixed part of the
                    // struct only (not the variable-length path that follows): 8 on x64, 6 on x86 Unicode.
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);

                    if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, out _, ref devInfoData))
                    {
                        var devicePath = Marshal.PtrToStringUni(detailBuffer + 4) ?? string.Empty;
                        var instanceId = GetInstanceId(devInfoData.DevInst);
                        if (!string.IsNullOrEmpty(instanceId))
                        {
                            var name = GetProductName(devInfoData.DevInst)
                                       ?? GetProperty(deviceInfoSet, ref devInfoData, SpdrpFriendlyName)
                                       ?? GetProperty(deviceInfoSet, ref devInfoData, SpdrpDeviceDesc)
                                       ?? devicePath;
                            results.Add(new HidHideDeviceInfo(instanceId, name));
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    private static string? GetInstanceId(uint devInst)
    {
        var buffer = new StringBuilder(512);
        var result = CM_Get_Device_ID(devInst, buffer, buffer.Capacity, 0);
        return result == CrSuccess ? buffer.ToString() : null;
    }

    /// <summary>
    /// A HID collection's own friendly name/description is usually a generic Windows-assigned
    /// string like "HID-compliant game controller" or "USB Input Device" — every button/LED/touchpad
    /// sub-interface on the same physical device gets one of those, indistinguishable from each
    /// other and unrelated to the product's actual name. The real product string (e.g. "Open Stick
    /// Community GP2040-CE (D-Input)") is what the device itself reports in its USB descriptors
    /// (iManufacturer/iProduct) — Windows surfaces that as DEVPKEY_Device_BusReportedDeviceDesc, and
    /// it usually only lands on a node a level or two up the device tree (the composite/interface
    /// node), not the leaf HID collection. This is the same property HidHide's own Configuration
    /// Client GUI reads. Walks up to a few ancestors looking for it, then falls back to the same
    /// ancestors' regular friendly name/description, and only returns null (falling back further,
    /// to this device's own name) if nothing usable turns up anywhere in the chain.
    /// </summary>
    private static string? GetProductName(uint devInst)
    {
        var chain = new List<uint> { devInst };
        var current = devInst;
        for (var i = 0; i < 4; i++)
        {
            if (CM_Get_Parent(out var parentDevInst, current, 0) != CrSuccess)
            {
                break;
            }
            chain.Add(parentDevInst);
            current = parentDevInst;
        }

        foreach (var node in chain)
        {
            var busReported = GetDevNodeStringProperty(node, DevpkeyDeviceBusReportedDeviceDesc);
            if (!string.IsNullOrEmpty(busReported))
            {
                return busReported;
            }
        }

        foreach (var node in chain.Skip(1))
        {
            var name = GetDevNodeStringProperty(node, DevpkeyDeviceFriendlyName)
                       ?? GetDevNodeStringProperty(node, DevpkeyDeviceDeviceDesc);
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return null;
    }

    private static string? GetDevNodeStringProperty(uint devInst, DEVPROPKEY propertyKey)
    {
        uint bufferSize = 0;
        CM_Get_DevNode_PropertyW(devInst, ref propertyKey, out _, IntPtr.Zero, ref bufferSize, 0);
        if (bufferSize == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (CM_Get_DevNode_PropertyW(devInst, ref propertyKey, out _, buffer, ref bufferSize, 0) != CrSuccess)
            {
                return null;
            }

            var text = Marshal.PtrToStringUni(buffer, (int)(bufferSize / 2)).TrimEnd('\0');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? GetProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfoData, int property)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref devInfoData, property, out _, buffer, buffer.Length, out var requiredSize) || requiredSize == 0)
        {
            return null;
        }
        var text = Encoding.Unicode.GetString(buffer, 0, (int)requiredSize).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DEVPROPKEY(Guid fmtid, uint pid);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, int ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_PropertyW(uint dnDevInst, ref DEVPROPKEY propertyKey, out uint propertyType, IntPtr propertyBuffer, ref uint propertyBufferSize, uint ulFlags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, int property, out uint propertyRegDataType, byte[] propertyBuffer, int propertyBufferSize, out uint requiredSize);

    [DllImport("cfgmgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID(uint devInst, StringBuilder buffer, int bufferLen, int flags);
}
