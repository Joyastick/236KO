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
                            var name = GetProperty(deviceInfoSet, ref devInfoData, SpdrpFriendlyName)
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

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

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
