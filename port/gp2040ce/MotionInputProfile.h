#pragma once

// Embedded stand-in for the Dictionary<string, ...> fields on Profile.cs (AttackBindings,
// AttackOutputs, KeyOutputs, MotionAttackOutputs). Requires MotionInputCore.h for MAX_MOTIONS.
//
// Design: a fixed-capacity array of {key, value} pairs, linear-scanned by strcmp. That's a
// deliberate trade against std::map/std::unordered_map:
//   - No heap allocation, no RB-tree/bucket-array RAM overhead - just N contiguous slots.
//   - These maps have a handful of entries (attack roles: light/medium/heavy; key outputs: maybe
//     10-15 buttons) and are read a few times per tick, not per sample in a hot loop, so an O(n)
//     strcmp scan over single digits of entries costs nothing worth optimizing.
//   - Populated once at profile-load time, not reallocated during play.

#include "MotionInputCore.h"

namespace MotionInput {

// ---- StringMap<TValue, N>: fixed-capacity replacement for Dictionary<string, TValue> ----

template <typename TValue, size_t N>
class StringMap {
public:
    // Inserts, or overwrites if the key is already present. No-ops silently past capacity N -
    // check Size() == N at profile-load time if you need to catch that instead.
    void Set(const char* key, const TValue& value) {
        for (size_t i = 0; i < _count; i++) {
            if (std::strcmp(_entries[i].key, key) == 0) {
                _entries[i].value = value;
                return;
            }
        }
        if (_count < N) {
            _entries[_count].key = key;
            _entries[_count].value = value;
            _count++;
        }
    }

    const TValue* Find(const char* key) const {
        for (size_t i = 0; i < _count; i++) {
            if (std::strcmp(_entries[i].key, key) == 0) return &_entries[i].value;
        }
        return nullptr;
    }

    size_t Size() const { return _count; }
    const char* KeyAt(size_t i) const { return _entries[i].key; }
    const TValue& ValueAt(size_t i) const { return _entries[i].value; }

private:
    struct Entry {
        const char* key = "";
        TValue value{};
    };
    Entry _entries[N];
    size_t _count = 0;
};

// ---- TokenList: fixed-capacity replacement for List<string> (a binding's output tokens) ----

constexpr size_t MAX_TOKENS_PER_BINDING = 4;

struct TokenList {
    const char* tokens[MAX_TOKENS_PER_BINDING] = {};
    size_t count = 0;

    void Add(const char* token) {
        if (count < MAX_TOKENS_PER_BINDING) tokens[count++] = token;
    }
};

// ---- Profile.cs's four dictionaries ----

constexpr size_t MAX_ATTACK_ROLES = 8;   // light/medium/heavy plus room to grow
constexpr size_t MAX_KEY_OUTPUTS = 16;   // one entry per remappable physical button

// role (e.g. "light") -> physical input ids that trigger it
using AttackBindingsMap = StringMap<TokenList, MAX_ATTACK_ROLES>;

// role -> output tokens, fired when the attack is pressed with no motion preceding it
using AttackOutputsMap = StringMap<TokenList, MAX_ATTACK_ROLES>;

// physical input id -> output tokens, direct passthrough/remap
using KeyOutputsMap = StringMap<TokenList, MAX_KEY_OUTPUTS>;

// MotionAttackOutputs is nested (motion name -> role -> tokens) in the C# version. Flattened here
// into "per-motion slot holds its own role->tokens map", indexed the same way MotionMatcher
// indexes MotionDefinition - by linear scan over motion name, since motion count is also small
// and fixed by MAX_MOTIONS.
class MotionAttackOutputsMap {
public:
    // Gets or creates the role->tokens map for a motion name. Use at profile-load time to
    // populate; use Find() (below) for read-only lookup during play.
    AttackOutputsMap& ForMotion(const char* motionName) {
        for (size_t i = 0; i < _count; i++) {
            if (std::strcmp(_entries[i].motionName, motionName) == 0) return _entries[i].perRole;
        }
        _entries[_count].motionName = motionName;
        return _entries[_count++].perRole;
    }

    const AttackOutputsMap* Find(const char* motionName) const {
        for (size_t i = 0; i < _count; i++) {
            if (std::strcmp(_entries[i].motionName, motionName) == 0) return &_entries[i].perRole;
        }
        return nullptr;
    }

private:
    struct Entry {
        const char* motionName = "";
        AttackOutputsMap perRole;
    };
    Entry _entries[MAX_MOTIONS];
    size_t _count = 0;
};

// Which physical inputs feed MapDirection's DirectionInputs, OR'd together - still meaningful on
// GP2040-CE (a board can have both a d-pad and an analog stick wired), just not tied to an
// XInput/DirectInput source selection the way ControllerInputSettings.SelectedControllerId and
// PollRateHz were (those two are dropped: GP2040-CE ticks on its own USB report cadence and IS the
// input source, there's nothing to select).
enum DirectionSource : uint8_t {
    DirectionSource_Dpad = 1 << 0,
    DirectionSource_LeftStick = 1 << 1,
    DirectionSource_RightStick = 1 << 2,
};

struct ControllerInputSettings {
    uint8_t directionSources = DirectionSource_Dpad | DirectionSource_LeftStick;
    double stickDeadzone = 0.35;
    double triggerThreshold = 0.35;
};

// ---- Profile: the embedded equivalent of Profile.cs ----

struct Profile {
    ControllerInputSettings controllerInput;  // reuse the struct as-is; see note above
    MotionLeniencySettings leniency;

    MotionDefinition motions[MAX_MOTIONS];
    size_t motionCount = 0;

    AttackBindingsMap attackBindings;
    MotionAttackOutputsMap motionAttackOutputs;
    AttackOutputsMap attackOutputs;
    KeyOutputsMap keyOutputs;
};

}  // namespace MotionInput

// ---------------------------------------------------------------------------------------------
// Example: populating a profile equivalent to the JSON example in 236KO's README
// (light/medium/heavy -> x/y/b, qcf+light -> [$controller_motion_final, controller:lt])
// ---------------------------------------------------------------------------------------------
//
//   using namespace MotionInput;
//
//   Profile profile;
//
//   profile.motions[0] = MotionDefinition{"qcf", {2,3,6}, 3, true, 0, 0};
//   profile.motionCount = 1;
//
//   TokenList lightBinding; lightBinding.Add("x");
//   profile.attackBindings.Set("light", lightBinding);
//
//   TokenList lightOutput; lightOutput.Add("controller:x");
//   profile.attackOutputs.Set("light", lightOutput);
//
//   TokenList qcfLightCombo;
//   qcfLightCombo.Add("$controller_motion_final");
//   qcfLightCombo.Add("controller:lt");
//   profile.motionAttackOutputs.ForMotion("qcf").Set("light", qcfLightCombo);
//
// Reading it back at match time (mirrors MotionInputEngine::TryFireCombo):
//
//   if (auto* perRole = profile.motionAttackOutputs.Find(match.motionName)) {
//       if (auto* tokens = perRole->Find("light")) {
//           OutputList resolved;
//           OutputResolver::Resolve(tokens->tokens, tokens->count, ctx, resolved);
//       }
//   }
//
// ---------------------------------------------------------------------------------------------
// Alternative worth considering instead of this string-keyed port
// ---------------------------------------------------------------------------------------------
// GP2040-CE already has closed, compile-time-known enums for buttons/dpad directions (its
// GamepadState bitmasks). If you don't need runtime-editable profiles (the 236KO desktop app's
// Profile Editor tab has no GP2040-CE equivalent - configuration there happens through its own
// web-config UI, which is a different problem), it's simpler and cheaper to skip string keys
// entirely: index AttackBindings/AttackOutputs/KeyOutputs by GP2040-CE's own button enum instead
// of by string ("light" -> GpioAction::BUTTON_PRESS_B1, etc.), turning each StringMap into a plain
// fixed array indexed by that enum's ordinal - O(1), no strcmp, no key management at all. The
// string-keyed version here exists to stay a faithful port of Profile.cs's shape; switch to
// enum-indexed arrays once you're building this for real inside GP2040-CE's own config model.
