using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using Reloaded.Injector;

namespace MotionInput.Core.Cloak;

/// <summary>
/// Hides one XInput controller slot from a single selected running process, without touching any
/// other process (including the game the player might want it hidden from vs. everything else).
///
/// Mechanism: injects CloakBootstrap.dll (native, hosts the CLR) into the target process, which
/// loads MotionInput.Cloak.Payload.dll and hooks XInputGetState/XInputGetStateEx to report "not
/// connected" for the configured user index. HidHide can't do this at all — it only filters the
/// HID class stack, which XInput bypasses entirely (see the Bindings tab's design notes).
///
/// Auto-revert: the hook checks for a named event that only exists while this service is alive
/// (see <see cref="Stop"/>/<see cref="Dispose"/>). If 236KO exits or crashes, the event disappears
/// and the hook starts passing through real controller state again within ~250ms — no explicit
/// "unhook" round-trip needed, and it survives an unclean shutdown.
/// </summary>
public sealed class ProcessCloakService : IDisposable
{
    private const string ConfigMapName = "Local\\236KO_CloakConfig";
    private const string LivenessEventName = "Local\\236KO_Cloak_Alive";

    private MemoryMappedFile? _configMmf;
    private EventWaitHandle? _livenessEvent;
    private Injector? _injector;

    public bool IsActive { get; private set; }

    /// <summary>
    /// Starts hiding <paramref name="xinputUserIndex"/> (0-3) from <paramref name="targetProcessId"/>.
    /// Requires CloakBootstrap.dll and its dependencies (MotionInput.Cloak.Payload.dll, nethost.dll,
    /// FASM*.DLL, Reloaded.Hooks*.dll) to be present in the "Cloak" folder next to the app exe.
    /// </summary>
    public void Start(int targetProcessId, int xinputUserIndex)
    {
        if (IsActive)
        {
            throw new InvalidOperationException("Cloak is already active. Call Stop() first.");
        }

        var bootstrapDllPath = Path.Combine(AppContext.BaseDirectory, "Cloak", "CloakBootstrap.dll");
        if (!File.Exists(bootstrapDllPath))
        {
            throw new FileNotFoundException("CloakBootstrap.dll not found — the cloak runtime wasn't shipped with this build.", bootstrapDllPath);
        }

        Process target;
        try
        {
            target = Process.GetProcessById(targetProcessId);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"No running process with id {targetProcessId}.");
        }

        _configMmf = MemoryMappedFile.CreateNew(ConfigMapName, 4);
        using (var accessor = _configMmf.CreateViewAccessor(0, 4))
        {
            accessor.Write(0, xinputUserIndex);
        }

        _livenessEvent = new EventWaitHandle(true, EventResetMode.ManualReset, LivenessEventName);

        _injector = new Injector(target);
        var handle = _injector.Inject(bootstrapDllPath);
        if (handle == 0)
        {
            Stop();
            throw new InvalidOperationException($"Injection into process {targetProcessId} failed (LoadLibraryW returned 0). The process may be protected (anti-cheat, elevated relative to this app) or already have a conflicting module loaded.");
        }

        IsActive = true;
    }

    /// <summary>
    /// Stops hiding the controller. The hook in the target process reverts to passing through real
    /// state within ~250ms of this call (it polls for the liveness event's existence, doesn't
    /// require an explicit unhook message) — same as if 236KO had simply exited or crashed.
    /// </summary>
    public void Stop()
    {
        _injector?.Dispose();
        _injector = null;

        _livenessEvent?.Reset();
        _livenessEvent?.Dispose();
        _livenessEvent = null;

        _configMmf?.Dispose();
        _configMmf = null;

        IsActive = false;
    }

    public void Dispose() => Stop();
}
