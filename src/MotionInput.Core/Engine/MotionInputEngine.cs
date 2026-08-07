using MotionInput.Core.Input;
using MotionInput.Core.Models;
using MotionInput.Core.Motion;
using MotionInput.Core.Output;

namespace MotionInput.Core.Engine;

/// <summary>
/// Top-level orchestrator: polls the controller, resolves a numpad direction each tick, feeds a
/// <see cref="MotionBuffer"/>/<see cref="MotionMatcher"/> pair, and drives the virtual pad from a
/// single "desired state, diffed each tick" pass — covering the d-pad mirror, plain attack/key
/// passthrough, and motion+attack combo outputs alike — so nothing ever fights over the same
/// button from two different code paths.
/// </summary>
public sealed class MotionInputEngine : IDisposable
{
    private readonly Profile _profile;
    private readonly IControllerSource _controllerSource;
    private readonly IVirtualGamepad _gamepad;
    private readonly MotionBuffer _buffer;
    private readonly MotionMatcher _matcher;
    private readonly HashSet<string> _previousDigital = new();

    // Everything currently held on the virtual pad/keyboard, diffed each tick against a freshly
    // computed "desired" set — press on the rising edge, release on the falling edge, hold in
    // between. Covers the d-pad, plain AttackOutputs/KeyOutputs passthrough, and active combo
    // outputs, all in one pass, so only one thing ever writes a given button per tick.
    private readonly HashSet<string> _heldMirroredButtons = new();
    private readonly HashSet<string> _heldMirroredKeys = new();

    // Attack roles whose current hold is being driven by a motion+attack combo instead of the
    // plain AttackOutputs mapping, plus the combo's own resolved outputs (frozen at the moment it
    // fired, e.g. the motion's start/final direction) — held for as long as the physical attack
    // button stays down, exactly like a plain attack would be.
    private readonly HashSet<string> _comboConsumedRoles = new();
    private readonly Dictionary<string, IReadOnlyList<PrimitiveOutput>> _comboSustainedOutputs = new();

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private PendingMotion? _pending;

    public MotionInputEngine(Profile profile, IControllerSource controllerSource, IVirtualGamepad gamepad)
    {
        _profile = profile;
        _controllerSource = controllerSource;
        _gamepad = gamepad;
        _buffer = new MotionBuffer();
        _matcher = new MotionMatcher(profile.Motions, profile.Leniency);
    }

    /// <summary>Raised on every poll with the raw snapshot, for UI display.</summary>
    public event Action<ControllerSnapshot>? SnapshotPolled;

    /// <summary>Raised whenever the resolved numpad direction changes.</summary>
    public event Action<int>? DirectionChanged;

    /// <summary>Raised the instant a motion's sequence completes, whether or not an attack follows.</summary>
    public event Action<MotionMatchResult>? MotionDetected;

    /// <summary>Raised whenever outputs are actually fired: motion name (or null for a bare attack) and the attack role (or null for a motion with no attack).</summary>
    public event Action<string?, string?>? OutputFired;

    public bool IsRunning => _thread is { IsAlive: true };

    public void Start()
    {
        if (IsRunning) return;

        _gamepad.Connect();
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Loop(_cts.Token)) { IsBackground = true, Name = "MotionInputEngine" };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;

        foreach (var button in _heldMirroredButtons) _gamepad.SetButton(button, false);
        foreach (var key in _heldMirroredKeys) KeySender.SetKey(key, false);
        _heldMirroredButtons.Clear();
        _heldMirroredKeys.Clear();
        _comboConsumedRoles.Clear();
        _comboSustainedOutputs.Clear();

        _gamepad.ResetAll();
        _buffer.Clear();
        _matcher.Reset();
        _pending = null;
        _previousDigital.Clear();
    }

    private void Loop(CancellationToken ct)
    {
        var pollRate = Math.Clamp(_profile.ControllerInput.PollRateHz, 30, 1000);
        var interval = TimeSpan.FromSeconds(1.0 / pollRate);
        var next = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            Tick();

            next += interval;
            var delay = next - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                Thread.Sleep(delay);
            }
            else
            {
                next = DateTime.UtcNow;
            }
        }
    }

    private void Tick()
    {
        var snapshot = _controllerSource.Poll();
        var now = snapshot.Timestamp;
        SnapshotPolled?.Invoke(snapshot);

        var direction = DirectionMapper.Map(snapshot, _profile.ControllerInput);

        if (_buffer.Update(direction, now))
        {
            DirectionChanged?.Invoke(direction);

            var match = _matcher.TryMatch(_buffer, now);
            if (match is not null)
            {
                _pending = new PendingMotion(match, now + TimeSpan.FromMilliseconds(_profile.Leniency.AttackWindowMs));
                MotionDetected?.Invoke(match);
            }
        }

        var desiredButtons = new HashSet<string>();
        var desiredKeys = new HashSet<string>();
        var dpadOverridden = false;

        foreach (var (role, physicalIds) in _profile.AttackBindings)
        {
            var wasHeld = physicalIds.Any(id => _previousDigital.Contains(id));
            var isHeld = physicalIds.Any(id => snapshot.Digital.Contains(id));

            if (isHeld && !wasHeld && TryFireCombo(role, now))
            {
                _comboConsumedRoles.Add(role);
            }

            if (!isHeld)
            {
                _comboConsumedRoles.Remove(role);
                _comboSustainedOutputs.Remove(role);
                continue;
            }

            // A press consumed by a motion+attack combo stays driven by the combo's own resolved
            // outputs (its d-pad override, its attack button, whatever it declared) for as long as
            // the physical button is down, instead of falling back to the plain AttackOutputs
            // mapping — same continuous-hold behavior a plain attack gets, just with the combo's
            // outputs in place of the ordinary ones.
            if (_comboConsumedRoles.Contains(role))
            {
                foreach (var output in _comboSustainedOutputs[role])
                {
                    if (output.Kind == PrimitiveOutputKind.ControllerButton)
                    {
                        desiredButtons.Add(output.Value);
                        if (IsDpadButton(output.Value)) dpadOverridden = true;
                    }
                    else
                    {
                        desiredKeys.Add(output.Value);
                    }
                }
                continue;
            }

            if (_profile.AttackOutputs.TryGetValue(role, out var attackTokens))
            {
                CollectDesired(attackTokens, new OutputContext { AttackControllerButton = ResolveAttackButton(role) }, desiredButtons, desiredKeys);
            }
        }

        foreach (var (physicalId, tokens) in _profile.KeyOutputs)
        {
            if (snapshot.Digital.Contains(physicalId))
            {
                CollectDesired(tokens, new OutputContext(), desiredButtons, desiredKeys);
            }
        }

        // The physical stick/d-pad direction mirrors onto the virtual d-pad by default; an active
        // combo that declared its own d-pad output (e.g. a forced neutral or specific direction)
        // takes over instead, so the two never fight over the same buttons in the same tick.
        if (!dpadOverridden)
        {
            foreach (var button in OutputResolver.DirectionButtons(direction))
            {
                desiredButtons.Add(button);
            }
        }

        ApplyContinuousMirror(desiredButtons, desiredKeys);

        _previousDigital.Clear();
        foreach (var id in snapshot.Digital)
        {
            _previousDigital.Add(id);
        }

        if (_pending is { } pending && now > pending.ExpiresAt)
        {
            _pending = null;
        }
    }

    /// <summary>
    /// Resolves and records the motion+attack combo's outputs if this press lands inside an active
    /// motion's attack window. Returns true if it did, so the caller drives this hold from the
    /// combo's outputs (via <see cref="_comboSustainedOutputs"/>) instead of the plain attack
    /// mapping for as long as the button stays down.
    /// </summary>
    private bool TryFireCombo(string role, DateTime now)
    {
        if (_pending is { } pending &&
            now <= pending.ExpiresAt &&
            _profile.MotionAttackOutputs.TryGetValue(pending.Match.MotionName, out var perRole) &&
            perRole.TryGetValue(role, out var comboTokens))
        {
            var context = new OutputContext
            {
                StartDirection = pending.Match.StartDirection,
                FinalDirection = pending.Match.FinalDirection,
                AttackControllerButton = ResolveAttackButton(role),
            };
            _comboSustainedOutputs[role] = OutputResolver.Resolve(comboTokens, context);

            OutputFired?.Invoke(pending.Match.MotionName, role);
            _pending = null;
            return true;
        }

        if (_profile.AttackOutputs.ContainsKey(role))
        {
            OutputFired?.Invoke(null, role);
        }
        return false;
    }

    private static void CollectDesired(List<string> tokens, OutputContext context, HashSet<string> desiredButtons, HashSet<string> desiredKeys)
    {
        foreach (var output in OutputResolver.Resolve(tokens, context))
        {
            if (output.Kind == PrimitiveOutputKind.ControllerButton)
            {
                desiredButtons.Add(output.Value);
            }
            else
            {
                desiredKeys.Add(output.Value);
            }
        }
    }

    private void ApplyContinuousMirror(HashSet<string> desiredButtons, HashSet<string> desiredKeys)
    {
        foreach (var button in _heldMirroredButtons)
        {
            if (!desiredButtons.Contains(button)) _gamepad.SetButton(button, false);
        }
        foreach (var button in desiredButtons)
        {
            if (!_heldMirroredButtons.Contains(button)) _gamepad.SetButton(button, true);
        }

        foreach (var key in _heldMirroredKeys)
        {
            if (!desiredKeys.Contains(key)) KeySender.SetKey(key, false);
        }
        foreach (var key in desiredKeys)
        {
            if (!_heldMirroredKeys.Contains(key)) KeySender.SetKey(key, true);
        }

        _heldMirroredButtons.Clear();
        _heldMirroredButtons.UnionWith(desiredButtons);
        _heldMirroredKeys.Clear();
        _heldMirroredKeys.UnionWith(desiredKeys);
    }

    private string? ResolveAttackButton(string role)
    {
        if (_profile.AttackOutputs.TryGetValue(role, out var tokens))
        {
            foreach (var token in tokens)
            {
                if (token.StartsWith("controller:", StringComparison.OrdinalIgnoreCase))
                {
                    return token["controller:".Length..].ToLowerInvariant();
                }
            }
        }
        return null;
    }

    private static bool IsDpadButton(string name) =>
        name is "dpad_up" or "dpad_down" or "dpad_left" or "dpad_right";

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private sealed record PendingMotion(MotionMatchResult Match, DateTime ExpiresAt);
}
