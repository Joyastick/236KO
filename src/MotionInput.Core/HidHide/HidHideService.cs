using Nefarius.Drivers.HidHide;

namespace MotionInput.Core.HidHide;

/// <summary>
/// Thin wrapper around the official Nefarius.Drivers.HidHide client library — the same one the
/// HidHide Configuration Client GUI uses — instead of shelling out to HidHideCLI.exe and parsing
/// its console output, which is what made hiding unreliable in the previous version of this tool.
/// </summary>
public sealed class HidHideService
{
    private readonly HidHideControlService _service = new();

    public bool IsInstalled => _service.IsInstalled;

    public bool IsOperational => _service.IsOperational;

    public bool CloakingEnabled
    {
        get => _service.IsActive;
        set => _service.IsActive = value;
    }

    public IReadOnlyList<string> BlockedInstanceIds => _service.BlockedInstanceIds;

    public IReadOnlyList<string> WhitelistedApplicationPaths => _service.ApplicationPaths;

    public IReadOnlyList<HidHideDeviceInfo> ListDevices() => HidHideDeviceEnumerator.List();

    public void CloakDevice(string instanceId)
    {
        if (!_service.BlockedInstanceIds.Contains(instanceId, StringComparer.OrdinalIgnoreCase))
        {
            _service.AddBlockedInstanceId(instanceId);
        }
    }

    public void UncloakDevice(string instanceId) => _service.RemoveBlockedInstanceId(instanceId);

    public void ClearCloakedDevices() => _service.ClearBlockedInstancesList();

    /// <summary>Whitelist an executable so it can still see the real controller while everything else is cloaked from it.</summary>
    public void AllowApplication(string exePath)
    {
        if (!_service.ApplicationPaths.Contains(exePath, StringComparer.OrdinalIgnoreCase))
        {
            _service.AddApplicationPath(exePath, false);
        }
    }

    public void RemoveApplication(string exePath) => _service.RemoveApplicationPath(exePath);

    /// <summary>
    /// Registers this app's own process so it keeps reading the real controller (which must stay
    /// whitelisted) while the target game's exe is left off the whitelist and therefore cloaked from
    /// the device once cloaking is enabled.
    /// </summary>
    public void AllowSelf() => AllowApplication(Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0]);
}
