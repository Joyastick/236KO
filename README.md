# 236KO

A fighting-game motion input reader for Windows. It watches a physical controller, recognizes
numpad-notation motions (`236` = quarter-circle forward, `623` = dragon punch, etc.), and — when a
motion completes with an attack button inside a configurable window — emits a different button
combination on an emulated Xbox 360 controller. Optionally, it can also hide your real controller
from the game via HidHide, so only the emulated pad is seen.
## Requirements

- Windows 10/11.
- [.NET 9 SDK](https://dotnet.microsoft.com/download) to build/run from source. Not needed to just
  run a published build — see [Building a standalone .exe](#building-a-standalone-exe).
- [ViGEmBus driver](https://github.com/ViGEm/ViGEmBus/releases) — required for the emulated
  controller output.
- [HidHide driver](https://github.com/nefarius/HidHide/releases) — optional, only needed if you want
  to hide your real controller from the game. HidHide only works on a controller running in
  DirectInput/generic-HID mode — it can't cloak XInput devices, since XInput bypasses the HID class
  stack entirely.

Both drivers are checked for at startup; 236KO won't open its main window until both are installed.

## Running

```
dotnet run --project src/MotionInput.App/MotionInput.App.csproj
```

## Building a standalone .exe

```
dotnet publish src/MotionInput.App/MotionInput.App.csproj -c Release -r win-x64
```

Produces a single self-contained `236KO.exe` (~60 MB, includes the .NET runtime, so the target
machine doesn't need .NET installed) at
`src/MotionInput.App/bin/Release/net9.0-windows/win-x64/publish/`. ViGEmBus and HidHide still need
to be installed separately — those are drivers, not something that can be bundled into the app.

## Using the app

- **Bindings**: press-to-bind wizard for all 2XKO inputs (directions, Light/Medium/Heavy/S1/S2/Tag/
  Start/Select/Dash/Break/Parry). Directions set which D-Pad/stick source is used; every other input
  captures whatever physical button you press, and separately lets you pick which virtual Xbox 360
  button it should fire - the two are independent, since a DirectInput controller's physical button
  ids (e.g. `button5`) aren't valid Xbox button names and can't just be passed straight through.
- **Hide from Game**: HidHide cloaking: lists connected HID devices, lets you pick which one(s) to
  cloak, toggle cloaking on/off, and manage the whitelist of applications (236KO itself, plus
  anything else you add) that can still see the real controller while everything else can't.
- **Monitor**: live view of the current direction, held inputs, raw analog/axis values, the
  pending motion/attack window, the last output sent to the virtual pad, and a recognition log.
- **Profile Editor**: full editable view of everything a profile holds: controller input settings,
  buffer/leniency timing, the motion list, attack bindings, and the Motion + Attack Outputs table
  (which direction+role combination fires for each recognized motion).

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

- **Diagonal-adjacency substitution**: a direction that's one numpad "click" away from what a
  motion step requires (e.g. `1` or `3` standing in for `2`) still satisfies that step. This is
  per-motion (`AllowDiagonalSkip`) so you can tighten specific motions if they're getting mis-read.
  It **never applies to a motion's final direction** — that one is always matched exactly. Motions
  that share directions in different orders (`dp` = `[6,2,3]`, `qcf` = `[2,3,6]`) would otherwise
  bleed into each other: `6` is ring-adjacent to `3`, so a fireball roll ending held on `6` would
  satisfy dp's "final ~3" requirement and fire a dragon punch instead of/alongside the fireball.
- **Max gap**: the longest allowed time between two consecutive required steps. Extra held frames
  or brief neutral blips between steps don't break the motion as long as they fit inside this
  window.
- **Max sequence time**: the total time budget from the first required step to the last.
- **Attack window**: after a motion completes, the matcher watches for an attack button for up to
  this long before giving up on combining them.
- **Cooldown**: the minimum time before the same motion can be recognized again, so one long roll
  through several directions doesn't fire the same special repeatedly.
- **Sample consumption on match**: once a motion is recognized, the direction samples that made it
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

Profiles are JSON files in a `Profiles/` folder next to the executable, editable either by hand,
through the Bindings tab's press-to-bind wizard, or through the Profile Editor tab. A profile has:

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

Output tokens (used in `MotionAttackOutputs`, `AttackOutputs`, `KeyOutputs`):

| Token | Meaning |
|---|---|
| `1`-`9` | D-pad press(es) for that numpad direction (diagonals press two buttons); `5` forces the d-pad to neutral, overriding whatever direction is physically held |
| `<role name>` | Whatever controller button that role (from Attack Bindings, e.g. `s1`) currently outputs — case-insensitive |
| `controller:<button>` | Literal press: `a b x y lb rb lt rt start back ls rs dpad_up dpad_down dpad_left dpad_right` |
| `controller_direction:<1-9>` | Same as the bare digit form, kept for explicitness |
| `$controller_motion_final` | D-pad press(es) for the matched motion's final direction |
| `$controller_motion_start` | D-pad press(es) for the matched motion's starting direction |
| `$attack` | The controller button the triggering attack role resolves to |
| anything else | A literal keyboard key name (`shift`, `enter`, `f1`, single letters/digits, …) |

A combo can be written role-first, e.g. `qcf` (`236`) + Light attack outputting `5` + `s1` — force
the d-pad to neutral and press whatever button S1 (bound in the Bindings tab) currently fires. The
role name is resolved through that role's own `AttackOutputs`, not the raw physical button it was
captured on, so remapping S1 later automatically updates every combo that references it.

Physical input ids read from a controller: `dpad_up/down/left/right`, `a b x y lb rb lt rt start
back ls rs`, `leftstick_x/y`, `rightstick_x/y`, `lefttrigger`, `righttrigger` for XInput pads;
DirectInput (non-XInput) pads expose generic `button0..N` plus the same `dpad_*` ids, derived from
any point-of-view (hat switch) controller the device reports — some browser/tester tools flatten
that hat's angle into an "axis" slot (commonly shown as axis 9) purely as a display convention, but
it's the same physical hat DirectInput reports, and it's read regardless of which of the device's
hat slots is the live one. DirectInput pads also expose raw values for every axis/slider/hat the
device has (`axis_x/y/z/rx/ry/rz`, `axis_slider0/1`, `pov0..3`) purely for diagnosis — watch the
Monitor tab's **Analog / Axis Values** readout while pressing a button one at a time to see which id
moves, if you need to work out a `button<N>` binding for a pad with a non-obvious layout.

## Hiding your controller from the game

If you don't want the game reacting to both your real controller and the emulated one, install
[HidHide](https://github.com/nefarius/HidHide/releases) and use the Hide from Game tab:

1. Switch your controller to DirectInput/generic-HID mode (hardware/firmware-dependent — GP2040-CE-
   based controllers and many fightsticks support this). HidHide can't cloak an XInput device.
2. Click **Refresh device list**, check **Cloak** next to your controller.
3. Click **Whitelist this app** — 236KO needs to keep reading the real controller even while
   everything else is cloaked from it.
4. Check **Cloaking enabled**.
5. Unplug/replug the controller (or restart the game) for the change to take effect.

Whitelisting and toggling cloaking both require Administrator the first time you do them.

## Project layout

- `src/MotionInput.Core` — all logic: input reading, direction mapping, motion buffer/matcher,
  output resolution/dispatch, ViGEm wrapper, HidHide wrapper, profile model/persistence. No UI
  dependency, fully unit-testable.
- `src/MotionInput.App` — WPF shell: Bindings wizard, Hide from Game (HidHide) tab, live monitor,
  profile editor.
- `src/MotionInput.Tests` — xUnit tests for the direction mapper, motion buffer/matcher, and
  DirectInput hat-switch decoding.

## Known limitations

- Diagonal leniency is *substitution* (an adjacent direction can stand in for a required step), not
  *skipping* (a required step vanishing from the buffer entirely because the player rolled through
  it too fast to register as its own sample). The timing-window leniency (max gap/sequence) covers
  most of the same real-world cases.
- DirectInput axis/button mapping for non-XInput pads uses reasonable defaults (primary stick =
  X/Y axes, first hat = d-pad) since DirectInput doesn't self-describe a "standard" layout the way
  XInput does; unusual pads may need `button<N>` bindings worked out by watching the Monitor tab's
  held-inputs readout while pressing buttons one at a time.
- HidHide cannot cloak XInput controllers under any circumstances — it only filters the Windows HID
  class stack, which XInput bypasses entirely. This is a Windows/driver-level limitation, not
  something 236KO can work around; the only fix is running the controller in DirectInput mode.
