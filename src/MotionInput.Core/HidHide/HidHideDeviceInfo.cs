namespace MotionInput.Core.HidHide;

/// <summary>A HID device interface as seen by SetupAPI, identified by the same instance-id format HidHide uses for cloaking.</summary>
public sealed record HidHideDeviceInfo(string InstanceId, string FriendlyName);
