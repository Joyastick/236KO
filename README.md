# 236KO

A fighting-game motion input reader for Windows. It watches a physical controller, recognizes
numpad-notation motions (`236` = quarter-circle forward, `623` = dragon punch, etc.), and — when a
motion completes with an attack button inside a configurable window — emits a different button
combination on an emulated Xbox 360 controller. Optionally, it can cloak the real controller from a
chosen game via [HidHide](https://github.com/nefarius/HidHide) so the game only ever sees the
emulated pad.

This is a from-scratch C#/.NET/WPF rewrite of the Python proof-of-concept in the sibling
`MotionInputs2XKO` repo, built to be more reliable (especially around HidHide, which used to shell
out to `HidHideCLI.exe` and parse its output) and to have a proper editable-profile UI.

## Requirements

- Windows 10/11.
- [.NET 9 SDK](https://dotnet.microsoft.com/download) to build/run from source.
- [ViGEmBus driver](https://github.com/ViGEm/ViGEmBus/releases) — required for the emulated
  controller output. Without it, `Start` will show an error.
- [HidHide](https://github.com/nefarius/HidHide/releases) — optional, only needed if you want to
  hide the real controller from a game. The app talks to it through the official
  `Nefarius.Drivers.HidHide` client library, not the CLI.

## Running

```
dotnet run --project src/MotionInput.App/MotionInput.App.csproj
```

Cloaking a device or whitelisting an application via the HidHide tab requires the app to run
elevated (Administrator), since that's what the HidHide driver requires.

## Building a standalone .exe

```
dotnet publish src/MotionInput.App/MotionInput.App.csproj -c Release -r win-x64
```

Produces a single self-contained `MotionInput.App.exe` (~60 MB, includes the .NET runtime, so the
target machine doesn't need .NET installed) at
`src/MotionInput.App/bin/Release/net9.0-windows/win-x64/publish/`. ViGEmBus and, if you want
cloaking, HidHide still need to be installed separately — those are drivers, not something that can
be bundled into the app.

## How it works

```
Controller (XInput or DirectInput)
  -> ControllerSnapshot (raw digital/analog state, backend-agnostic ids)
  -> DirectionMapper (dpad/stick -> numpad 1-9, with SOCD cancellation)
  -> MotionBuffer (rolling history of direction *changes*, timestamped)
  -> MotionMatcher (subsequence search against each profile motion, in priority order)
  -> attack-window watch (an attack button press within N ms combines with the motion)
  -> OutputResolver (profile tokens -> concrete controller-button/keyboard-key presses)
  -> VirtualGamepad (ViGEmBus) and/or KeySender
```

### Buffer / leniency system

This is a different implementation from the Python version's, though it aims at the same feel:

- **Diagonal-adjacency substitution** — a direction that's one numpad "click" away from what a
  motion step requires (e.g. `1` or `3` standing in for `2`) still satisfies that step. This is
  per-motion (`AllowDiagonalSkip`) so you can tighten specific motions if they're getting mis-read.
  It **never applies to a motion's final direction** — that one is always matched exactly. Motions
  that share directions in different orders (`dp` = `[6,2,3]`, `qcf` = `[2,3,6]`) would otherwise
  bleed into each other: `6` is ring-adjacent to `3`, so a fireball roll ending held on `6` would
  satisfy dp's "final ~3" requirement and fire a dragon punch instead of/alongside the fireball.
- **Max gap** — the longest allowed time between two consecutive required steps. Extra held frames
  or brief neutral blips between steps don't break the motion as long as they fit inside this
  window.
- **Max sequence time** — the total time budget from the first required step to the last.
- **Attack window** — after a motion completes, the matcher watches for an attack button for up to
  this long before giving up on combining them.
- **Cooldown** — the minimum time before the same motion can be recognized again, so one long roll
  through several directions doesn't fire the same special repeatedly.
- **Sample consumption on match** — once a motion is recognized, the direction samples that made it
  up are dropped from the buffer (the currently-held direction itself is left alone, so it doesn't
  spuriously re-register as a fresh change). Without this, a motion whose sequence is a suffix of
  another's — dp `[6,2,3]` vs. qcf `[2,3,6]` — could fire twice off the same roll: doing a clean dp
  and then naturally letting the stick settle back to forward would leave `[6,2,3,6]` in the buffer,
  and `[2,3,6]` in there still reads as a complete qcf. Consuming dp's samples on its own match means
  that trailing `6` starts a new, empty history instead of completing a second motion.

All four are global defaults in a profile's Leniency section; `MaxSequenceMs`/`MaxGapMs` can be
overridden per motion.

A motion is recognized the instant its *final* required direction becomes the buffer's most recent
sample — detection is immediate, not something polled after the fact.

### Profiles

Profiles are JSON files in a `Profiles/` folder next to the executable, editable either by hand or
through the Profile Editor tab. A profile has:

- `ControllerInput` — which of `dpad`/`left_stick`/`right_stick` feed the direction mapper, stick
  deadzone, trigger threshold, poll rate.
- `Leniency` — the buffer/leniency settings above.
- `Motions` — ordered list of `{ Name, Sequence: [numpad digits], AllowDiagonalSkip, MaxSequenceMs, MaxGapMs }`.
  Order is priority: earlier entries are tried first when more than one motion could match.
- `AttackBindings` — attack role (e.g. `light`) -> physical input ids that trigger it.
- `MotionAttackOutputs` — motion name -> attack role -> output tokens, fired when the attack lands
  inside the motion's attack window.
- `AttackOutputs` — attack role -> output tokens, held on the virtual pad for as long as the
  physical button is held (as long as that press wasn't already consumed by a combo below).
- `KeyOutputs` — physical input id -> output tokens, direct passthrough/remap of any other button,
  also held for as long as the physical button is held.

`AttackOutputs`, `KeyOutputs`, and `MotionAttackOutputs` are all true passthrough: hold the physical
button, the mapped output stays held on the virtual pad for as long as you hold it, same as a real
controller would. For a motion+attack combo, that means the combo's *entire* resolved output (its
d-pad override included, if it has one) stays held for as long as the triggering attack button does
— not just its attack-button token. Releasing the attack button releases everything the combo held.

Internally this all comes from one "desired state, diffed each tick" pass per poll — the d-pad
mirror, plain attack/key passthrough, and active combo outputs are resolved into a single set and
compared against what's currently held, so nothing ever fights over the same button from two
different code paths (that used to cause the d-pad to flicker between a combo's forced direction and
the physically-held one).

Output tokens (used in `MotionAttackOutputs`, `AttackOutputs`, `KeyOutputs`):

| Token | Meaning |
|---|---|
| `controller:<button>` | Literal press: `a b x y lb rb lt rt start back ls rs dpad_up dpad_down dpad_left dpad_right` |
| `controller_direction:<1-9>` | D-pad press(es) for an explicit numpad direction (diagonals press two buttons) |
| `$controller_motion_final` | D-pad press(es) for the matched motion's final direction |
| `$controller_motion_start` | D-pad press(es) for the matched motion's starting direction |
| `$attack` | The controller button the triggering attack role resolves to |
| anything else | A literal keyboard key name (`shift`, `enter`, `f1`, single letters/digits, …) |

Physical input ids read from a controller: `dpad_up/down/left/right`, `a b x y lb rb lt rt start
back ls rs`, `leftstick_x/y`, `rightstick_x/y`, `lefttrigger`, `righttrigger` for XInput pads;
DirectInput (non-XInput) pads expose generic `button0..N` plus the same `dpad_*` ids from the hat
switch, so bindings work the same way once you know which button number is which.

### Hiding the real controller

1. Install HidHide and reboot if prompted.
2. In the HidHide tab, hit **Refresh device list**, tick the physical controller's entries, and
   point **Target application** at the game/launcher `.exe`.
3. Click **Whitelist this app** so 236KO itself keeps reading the real controller once cloaking is
   on, then check **Cloaking enabled** and **Apply**.
4. Unplug/replug the controller and (re)launch the game.

Device enumeration goes straight through SetupAPI/CfgMgr32 rather than shelling out to
`HidHideCLI.exe`, which is where the Python version's HidHide support ran into trouble.

## Project layout

- `src/MotionInput.Core` — all logic: input reading, direction mapping, motion buffer/matcher,
  output resolution/dispatch, ViGEm and HidHide wrappers, profile model/persistence. No UI
  dependency, fully unit-testable.
- `src/MotionInput.App` — WPF shell: controller/profile selection, live monitor, profile editor,
  HidHide panel.
- `src/MotionInput.Tests` — xUnit tests for the direction mapper and motion buffer/matcher.

## Known limitations

- Diagonal leniency is *substitution* (an adjacent direction can stand in for a required step), not
  *skipping* (a required step vanishing from the buffer entirely because the player rolled through
  it too fast to register as its own sample). The timing-window leniency (max gap/sequence) covers
  most of the same real-world cases.
- DirectInput axis/button mapping for non-XInput pads uses reasonable defaults (primary stick =
  X/Y axes, first hat = d-pad) since DirectInput doesn't self-describe a "standard" layout the way
  XInput does; unusual pads may need `button<N>` bindings worked out by watching the Monitor tab's
  held-inputs readout while pressing buttons one at a time.
