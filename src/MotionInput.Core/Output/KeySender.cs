using System.Runtime.InteropServices;

namespace MotionInput.Core.Output;

/// <summary>Simulates keyboard key presses for profiles that want a literal keyboard output instead of/alongside a controller press.</summary>
public static class KeySender
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const uint KeyEventFKeyUp = 0x0002;

    public static void SetKey(string name, bool pressed)
    {
        var vk = MapKey(name);
        if (vk is null) return;
        keybd_event(vk.Value, 0, pressed ? 0u : KeyEventFKeyUp, UIntPtr.Zero);
    }

    private static byte? MapKey(string name)
    {
        var key = name.Trim().ToLowerInvariant();

        if (key.Length == 1)
        {
            var c = key[0];
            if (c is >= 'a' and <= 'z') return (byte)(0x41 + (c - 'a'));
            if (c is >= '0' and <= '9') return (byte)(0x30 + (c - '0'));
        }

        return key switch
        {
            "shift" => 0x10,
            "ctrl" or "control" => 0x11,
            "alt" => 0x12,
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "f1" => 0x70,
            "f2" => 0x71,
            "f3" => 0x72,
            "f4" => 0x73,
            "f5" => 0x74,
            "f6" => 0x75,
            "f7" => 0x76,
            "f8" => 0x77,
            "f9" => 0x78,
            "f10" => 0x79,
            "f11" => 0x7A,
            "f12" => 0x7B,
            _ => null,
        };
    }
}
