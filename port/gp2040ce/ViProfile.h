#pragma once

// Port of Profiles/Vi.json into the embedded Profile shape (MotionInputProfile.h). Faithful to
// every field that has a GP2040-CE equivalent; one thing is deliberately dropped, a Windows-side
// concern with no on-device analog:
//   - ControllerInput.SelectedControllerId / PollRateHz: XInput/DirectInput device selection and
//     software poll rate don't apply to firmware that ticks on its own USB report cadence and IS
//     the input source.

#include <initializer_list>

#include "MotionInputProfile.h"

namespace MotionInput {

inline MotionDefinition MakeMotion(const char* name, std::initializer_list<uint8_t> seq, bool allowDiagonalSkip = true) {
    MotionDefinition m;
    m.name = name;
    size_t i = 0;
    for (uint8_t d : seq) {
        if (i < MAX_SEQUENCE_LEN) m.sequence[i++] = d;
    }
    m.sequenceLen = (uint8_t)i;
    m.allowDiagonalSkip = allowDiagonalSkip;
    return m;
}

inline TokenList MakeTokens(std::initializer_list<const char*> toks) {
    TokenList t;
    for (auto tok : toks) t.Add(tok);
    return t;
}

inline Profile BuildViProfile() {
    Profile p;

    // ControllerInput: dpad + left_stick, same deadzone/threshold as the JSON.
    p.controllerInput.directionSources = DirectionSource_Dpad | DirectionSource_LeftStick;
    p.controllerInput.stickDeadzone = 0.35;
    p.controllerInput.triggerThreshold = 0.35;

    // Leniency: identical to the JSON, which just restates the global defaults.
    p.leniency.maxSequenceMs = 500;
    p.leniency.maxGapMs = 250;
    p.leniency.attackWindowMs = 300;
    p.leniency.motionCooldownMs = 150;

    // Motions, in priority order (matches the JSON array order).
    p.motions[0] = MakeMotion("dp", {6, 2, 3});
    p.motions[1] = MakeMotion("rdp", {4, 2, 1});
    p.motions[2] = MakeMotion("half_circle_forward", {4, 1, 2, 3, 6});
    p.motions[3] = MakeMotion("half_circle_back", {6, 3, 2, 1, 4});
    p.motions[4] = MakeMotion("qcf", {2, 3, 6});
    p.motions[5] = MakeMotion("qcb", {2, 1, 4});
    p.motionCount = 6;

    // AttackBindings: physical button -> role.
    p.attackBindings.Set("light", MakeTokens({"x"}));
    p.attackBindings.Set("medium", MakeTokens({"y"}));
    p.attackBindings.Set("heavy", MakeTokens({"rb"}));

    // AttackOutputs: bare attack (no preceding motion) -> output.
    p.attackOutputs.Set("light", MakeTokens({"controller:x"}));
    p.attackOutputs.Set("medium", MakeTokens({"controller:y"}));
    p.attackOutputs.Set("heavy", MakeTokens({"controller:rb"}));

    // KeyOutputs: direct passthrough for everything else.
    p.keyOutputs.Set("a", MakeTokens({"controller:a"}));
    p.keyOutputs.Set("b", MakeTokens({"controller:b"}));
    p.keyOutputs.Set("lb", MakeTokens({"controller:lb"}));
    p.keyOutputs.Set("ls", MakeTokens({"controller:ls"}));
    p.keyOutputs.Set("rs", MakeTokens({"controller:rs"}));
    p.keyOutputs.Set("start", MakeTokens({"controller:start"}));
    p.keyOutputs.Set("back", MakeTokens({"controller:back"}));

    // MotionAttackOutputs: motion + attack role -> combo output.
    p.motionAttackOutputs.ForMotion("qcf").Set("light", MakeTokens({"controller_direction:5", "controller:a"}));
    p.motionAttackOutputs.ForMotion("qcf").Set("medium", MakeTokens({"controller_direction:5", "controller:a"}));
    p.motionAttackOutputs.ForMotion("qcf").Set("heavy", MakeTokens({"controller:a", "controller:b"}));

    p.motionAttackOutputs.ForMotion("qcb").Set("light", MakeTokens({"controller_direction:4", "controller:a"}));
    p.motionAttackOutputs.ForMotion("qcb").Set("medium", MakeTokens({"$controller_motion_final", "controller:b"}));
    p.motionAttackOutputs.ForMotion("qcb").Set("heavy", MakeTokens({"controller:a", "controller:b"}));

    p.motionAttackOutputs.ForMotion("dp").Set("light", MakeTokens({"controller_direction:6", "controller:a"}));
    p.motionAttackOutputs.ForMotion("dp").Set("medium", MakeTokens({"controller_direction:2", "controller:b"}));
    p.motionAttackOutputs.ForMotion("dp").Set("heavy", MakeTokens({"controller_direction:2", "controller:a"}));

    p.motionAttackOutputs.ForMotion("rdp").Set("light", MakeTokens({"controller_direction:4", "controller:a"}));
    p.motionAttackOutputs.ForMotion("rdp").Set("medium", MakeTokens({"controller_direction:2", "controller:b"}));
    p.motionAttackOutputs.ForMotion("rdp").Set("heavy", MakeTokens({"controller_direction:2", "controller:a"}));

    p.motionAttackOutputs.ForMotion("half_circle_forward").Set("light", MakeTokens({"controller:x", "controller:a"}));
    p.motionAttackOutputs.ForMotion("half_circle_forward").Set("medium", MakeTokens({"controller:x", "controller:b"}));
    p.motionAttackOutputs.ForMotion("half_circle_forward").Set("heavy", MakeTokens({"controller:a", "controller:b"}));

    p.motionAttackOutputs.ForMotion("half_circle_back").Set("light", MakeTokens({"controller:x", "controller:a"}));
    p.motionAttackOutputs.ForMotion("half_circle_back").Set("medium", MakeTokens({"controller:x", "controller:b"}));
    p.motionAttackOutputs.ForMotion("half_circle_back").Set("heavy", MakeTokens({"controller:a", "controller:b"}));

    return p;
}

}  // namespace MotionInput
