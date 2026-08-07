using MotionInput.Core.Input;
using MotionInput.Core.Models;
using MotionInput.Core.Motion;
using MotionInput.Core.Output;

namespace MotionInput.Core.Engine;

/// <summary>
/// Top-level orchestrator: polls the controller, resolves a numpad direction each tick, feeds a
/// <see cref="MotionBuffer"/>/<see cref="MotionMatcher"/> pair, mirrors the direction onto the
/// virtual pad's d-pad, and fires configured outputs when a motion completes and an attack button
/// lands inside its attack window (or immediately, for a bare attack press).
/// </summary>
public sealed class MotionInputEngine : IDisposable
{
    private readonly Profile _profile;
    private readonly IControllerSource _controllerSource;
    private readonly IVirtualGamepad _gamepad;
    private readonly OutputDispatcher _dispatcher;
    private readonly MotionBuffer _buffer;
    private readonly MotionMatcher _matcher;
    private readonly HashSet<string> _previousDigital = new();

    // Buttons/keys currently held by continuous passthrough (KeyOutputs, and AttackOutputs when no
    // motion combo consumed the press) — diffed each tick like the d-pad, so holding a physical
    // button holds the mapped output for as long as it's held, rather than just pulsing it once.
    private readonly HashSet<string> _heldMirroredButtons = new();
    private readonly HashSet<string> _heldMirroredKeys = new();

    // Attack roles whose current hold was already consumed by a motion+attack combo macro, so they
    // don't *also* get continuously mirrored as a plain attack for the rest of that same hold.
    private readonly HashSet<string> _comboConsumedRoles = new();

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private PendingMotion? _pending;

    public MotionInputEngine(Profile profile, IControllerSource controllerSource, IVirtualGamepad gamepad)
    {
        _profile = profile;
        _controllerSource = controllerSource;
        _gamepad = gamepad;
        _dispatcher = new OutputDispatcher(gamepad);
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
        MirrorDpad(direction);

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
                continue;
            }

            // A press already spent on a motion+attack combo macro doesn't also hold the plain
            // attack output for the rest of that same hold — only a fresh press/release does.
            if (_comboConsumedRoles.Contains(role)) continue;

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
    /// Fires the motion+attack combo macro if this press lands inside an active motion's attack
    /// window. Returns true if it did (so the caller knows this hold shouldn't also be mirrored as
    /// a plain attack). A press that isn't part of a combo is left for the continuous-mirror loop
    /// in <see cref="Tick"/> to handle as an ordinary held button, not a one-shot pulse.
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
            Fire(comboTokens, context);
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

    private void Fire(List<string> tokens, OutputContext context)
    {
        var resolved = OutputResolver.Resolve(tokens, context);
        _ = _dispatcher.FireAsync(resolved, holdMs: 50);
    }

    private void MirrorDpad(int direction)
    {
        foreach (var button in new[] { "dpad_up", "dpad_down", "dpad_left", "dpad_right" })
        {
            _gamepad.SetButton(button, false);
        }
        foreach (var button in OutputResolver.DirectionButtons(direction))
        {
            _gamepad.SetButton(button, true);
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private sealed record PendingMotion(MotionMatchResult Match, DateTime ExpiresAt);
}
