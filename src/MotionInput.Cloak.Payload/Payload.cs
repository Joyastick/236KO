using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Reloaded.Hooks;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;

namespace MotionInput.Cloak.Payload;

/// <summary>
/// Loaded into the target game process by CloakBootstrap.dll (native, hosts the CLR). Hooks
/// XInputGetState/XInputGetStateEx so one XInput user index reports "not connected" to this
/// process only, while every other slot (including the ViGEm-emulated pad) passes through
/// untouched. Auto-reverts if 236KO exits or crashes: it doesn't rely on an explicit unhook
/// message, just a named event that only exists while 236KO is alive (see <see cref="IsHostAlive"/>).
/// </summary>
public static class Payload
{
    private const string ConfigMapName = "Local\\236KO_CloakConfig";
    private const string LivenessEventName = "Local\\236KO_Cloak_Alive";
    private const uint ErrorDeviceNotConnected = 1167;

    private static int _hiddenUserIndex = -1;
    private static IHook<XInputGetStateFn>? _getStateHook;
    private static IHook<XInputGetStateExFn>? _getStateExHook;

    private static DateTime _lastLivenessCheck = DateTime.MinValue;
    private static bool _lastLivenessResult;

    [Function(CallingConventions.Microsoft)]
    private delegate uint XInputGetStateFn(uint dwUserIndex, IntPtr pState);

    [Function(CallingConventions.Microsoft)]
    private delegate uint XInputGetStateExFn(uint dwUserIndex, IntPtr pState);

    /// <summary>
    /// Native hosting entry point (matches hostfxr's component_entry_point_fn signature). Reads
    /// which user index to hide from the config memory-mapped file, then installs the hooks.
    /// Called once, on a thread the native bootstrap spun up (never from DllMain itself).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "InstallCloak")]
    public static int InstallCloak(IntPtr arg, int argSizeInBytes)
    {
        try
        {
            _hiddenUserIndex = ReadHiddenUserIndex();
            if (_hiddenUserIndex < 0)
            {
                return -1;
            }

            InstallHooks();
            return 0;
        }
        catch (Exception ex)
        {
            // Never let an exception unwind into native code across the CLR boundary; a failed
            // cloak should just mean "nothing gets hidden", not a crashed game process.
            DebugLog($"InstallCloak exception: {ex}");
            return -2;
        }
    }

    // Debug-only: an injected DLL has no console, so this is the only way to see what happened.
    private static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText(@"C:\Temp\236KO_cloak_debug_managed.log", $"[pid={Environment.ProcessId}] {message}\n");
        }
        catch
        {
            // best-effort
        }
    }

    private static int ReadHiddenUserIndex()
    {
        using var mmf = MemoryMappedFile.OpenExisting(ConfigMapName, MemoryMappedFileRights.Read);
        using var accessor = mmf.CreateViewAccessor(0, 4, MemoryMappedFileAccess.Read);
        return accessor.ReadInt32(0);
    }

    private static void InstallHooks()
    {
        var xinputModule = FindLoadedXInputModule();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (xinputModule == IntPtr.Zero && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(250);
            xinputModule = FindLoadedXInputModule();
        }

        if (xinputModule == IntPtr.Zero)
        {
            return; // Game never loaded an XInput DLL within the wait window; nothing to hook.
        }

        var getStateAddr = GetProcAddress(xinputModule, "XInputGetState");
        if (getStateAddr != IntPtr.Zero)
        {
            _getStateHook = ReloadedHooks.Instance.CreateHook<XInputGetStateFn>(OnXInputGetState, (long)getStateAddr).Activate();
        }

        // XInputGetStateEx is exported by ordinal 100 (undocumented, but stable since Windows
        // Vista) — used by some engines/games to also read the Guide button.
        var getStateExAddr = GetProcAddress(xinputModule, 100);
        if (getStateExAddr != IntPtr.Zero)
        {
            _getStateExHook = ReloadedHooks.Instance.CreateHook<XInputGetStateExFn>(OnXInputGetStateEx, (long)getStateExAddr).Activate();
        }
    }

    private static uint OnXInputGetState(uint dwUserIndex, IntPtr pState)
    {
        if (dwUserIndex == _hiddenUserIndex && IsHostAlive())
        {
            return ErrorDeviceNotConnected;
        }
        return _getStateHook!.OriginalFunction(dwUserIndex, pState);
    }

    private static uint OnXInputGetStateEx(uint dwUserIndex, IntPtr pState)
    {
        if (dwUserIndex == _hiddenUserIndex && IsHostAlive())
        {
            return ErrorDeviceNotConnected;
        }
        return _getStateExHook!.OriginalFunction(dwUserIndex, pState);
    }

    /// <summary>
    /// True while 236KO is running. Re-checked at most every 250ms (games can poll XInput at
    /// hundreds of Hz) rather than on every call — the named event only exists while 236KO holds
    /// a handle to it, so once it exits (even by crashing), this naturally starts returning false
    /// and every hook call falls through to the real, unfiltered controller state again.
    /// </summary>
    private static bool IsHostAlive()
    {
        var now = DateTime.UtcNow;
        if (now - _lastLivenessCheck < TimeSpan.FromMilliseconds(250))
        {
            return _lastLivenessResult;
        }
        _lastLivenessCheck = now;

        // Deliberately does NOT cache the handle: a handle held here would itself keep the named
        // event alive, so 236KO closing its own handle would never actually make the object
        // disappear. Open fresh and dispose immediately every check instead — existence alone
        // (not signaled state) is the liveness signal.
        try
        {
            using var handle = EventWaitHandle.OpenExisting(LivenessEventName);
            _lastLivenessResult = true;
        }
        catch
        {
            _lastLivenessResult = false;
        }

        return _lastLivenessResult;
    }

    private static IntPtr FindLoadedXInputModule()
    {
        foreach (var name in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll", "xinput1_2.dll", "xinput1_1.dll" })
        {
            var handle = GetModuleHandle(name);
            if (handle != IntPtr.Zero) return handle;
        }
        return IntPtr.Zero;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr ordinal);

    private static IntPtr GetProcAddress(IntPtr hModule, int ordinal) => GetProcAddress(hModule, (IntPtr)ordinal);
}
