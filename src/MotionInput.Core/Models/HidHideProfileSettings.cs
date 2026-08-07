namespace MotionInput.Core.Models;

public sealed class HidHideProfileSettings
{
    /// <summary>Full path to the game/launcher executable that should be denied the real controller.</summary>
    public string? ApplicationPath { get; set; }

    /// <summary>HidHide device instance ids to cloak (see HidHideDeviceEnumerator).</summary>
    public List<string> DeviceInstanceIds { get; set; } = new();

    public bool CloakingEnabled { get; set; }
}
