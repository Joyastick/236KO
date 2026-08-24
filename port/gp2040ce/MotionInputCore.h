#pragma once

// Embedded C++ port of MotionInput.Core's DirectionMapper -> MotionBuffer -> MotionMatcher ->
// OutputResolver pipeline (see src/MotionInput.Core), for use inside GP2040-CE's on-device loop.
//
// Differences from the C# original, all mechanical (no behavior change):
//   - DateTime            -> uint32_t millis (caller supplies, e.g. to_ms_since_boot(get_absolute_time()))
//   - List<T>/Dictionary   -> fixed-capacity arrays (no heap allocation, no STL containers)
//   - string tokens        -> still char* tokens for OutputResolver, to stay a faithful 1:1 port;
//                             see the integration note at the bottom about precomputing these once
//                             instead of parsing every tick.
//
// Not included: profile JSON parsing, ViGEm/keyboard output, engine orchestration
// (MotionInputEngine) - those are Windows-side concerns with no GP2040-CE equivalent. This file is
// only the motion-recognition core.

#include <cstdint>
#include <cstddef>
#include <cstring>
#include <cstdlib>

namespace MotionInput {

constexpr uint8_t NEUTRAL = 5;
constexpr size_t BUFFER_CAPACITY = 32;   // MotionBuffer default capacity in the C# version
constexpr size_t MAX_SEQUENCE_LEN = 8;   // longest supported motion (qcf=3, dp=3, 360=8, etc.)
constexpr size_t MAX_MOTIONS = 16;       // profile motion count ceiling

// ---------------------------------------------------------------------------------------------
// DirectionMapper
// ---------------------------------------------------------------------------------------------

struct DirectionInputs {
    bool up = false;
    bool down = false;
    bool left = false;
    bool right = false;
};

// Reduces held up/down/left/right into a single numpad direction (1-9, 5=neutral), with SOCD
// cancellation (opposite directions on the same axis neutralize). Caller is responsible for OR-ing
// together whichever sources (dpad / left stick / right stick) the profile enables and applying
// deadzone thresholds before calling this - that's a config/profile concern, not core logic.
inline uint8_t MapDirection(DirectionInputs in) {
    if (in.up && in.down) { in.up = false; in.down = false; }
    if (in.left && in.right) { in.left = false; in.right = false; }

    if (in.up && in.left) return 7;
    if (in.up && in.right) return 9;
    if (in.up) return 8;
    if (in.down && in.left) return 1;
    if (in.down && in.right) return 3;
    if (in.down) return 2;
    if (in.left) return 4;
    if (in.right) return 6;
    return NEUTRAL;
}

inline void ToAxes(uint8_t numpad, int8_t& x, int8_t& y) {
    switch (numpad) {
        case 7: x = -1; y = 1;  break;
        case 8: x = 0;  y = 1;  break;
        case 9: x = 1;  y = 1;  break;
        case 4: x = -1; y = 0;  break;
        case 6: x = 1;  y = 0;  break;
        case 1: x = -1; y = -1; break;
        case 2: x = 0;  y = -1; break;
        case 3: x = 1;  y = -1; break;
        default: x = 0; y = 0;  break;
    }
}

// True if `candidate` is one numpad "click" away from `required` (Manhattan distance 1 on the
// compass), used for diagonal-skip leniency on non-final motion steps.
inline bool IsAdjacent(uint8_t candidate, uint8_t required) {
    if (candidate == required) return true;
    if (candidate == NEUTRAL || required == NEUTRAL) return false;

    int8_t cx, cy, rx, ry;
    ToAxes(candidate, cx, cy);
    ToAxes(required, rx, ry);

    int dx = cx > rx ? cx - rx : rx - cx;
    int dy = cy > ry ? cy - ry : ry - cy;
    return (dx + dy) == 1;
}

// ---------------------------------------------------------------------------------------------
// MotionBuffer
// ---------------------------------------------------------------------------------------------

struct DirectionSample {
    uint8_t direction = NEUTRAL;
    uint32_t startedAtMs = 0;
    uint32_t endedAtMs = 0;   // meaningful only when ended == true
    bool ended = false;
};

// Rolling ring-buffer history of numpad direction *changes* (not every poll tick) - what the
// matcher needs is "what directions were pressed, and in what order". A max age keeps very old
// input from lingering.
class MotionBuffer {
public:
    explicit MotionBuffer(uint32_t maxAgeMs = 2000) : _maxAgeMs(maxAgeMs) {}

    uint8_t Current() const { return _current; }
    size_t Size() const { return _count; }

    // Feed the latest resolved direction. Returns true if this was a change (new sample recorded).
    bool Update(uint8_t direction, uint32_t nowMs, bool recordNeutral = false) {
        if (direction == _current) {
            return false;
        }

        if (_count > 0 && !_samples[Tail()].ended) {
            _samples[Tail()].endedAtMs = nowMs;
            _samples[Tail()].ended = true;
        }

        _current = direction;

        if (direction != NEUTRAL || recordNeutral) {
            Push(DirectionSample{direction, nowMs, 0, false});
        }

        Trim(nowMs);
        return true;
    }

    // Oldest-first access, index in [0, Size()). Closes off a still-open final sample at asOfMs,
    // mirroring the C# Snapshot() behavior without allocating a copy of the whole buffer.
    DirectionSample At(size_t index, uint32_t asOfMs) const {
        DirectionSample s = _samples[(_head + index) % BUFFER_CAPACITY];
        if (!s.ended) {
            s.endedAtMs = asOfMs;
            s.ended = true;
        }
        return s;
    }

    void Clear() {
        _head = 0;
        _count = 0;
        _current = NEUTRAL;
    }

    // Drops recorded history without resetting the currently-tracked direction, so a still-held
    // direction doesn't spuriously re-register as a fresh change on the next tick. Call after a
    // motion is recognized so its samples can't also satisfy a different motion sharing a suffix
    // (e.g. dp's [6,2,3] rolling back to 6 would otherwise still contain qcf's [2,3,6]).
    void ConsumeHistory() {
        _head = 0;
        _count = 0;
    }

private:
    DirectionSample _samples[BUFFER_CAPACITY];
    size_t _head = 0;
    size_t _count = 0;
    uint32_t _maxAgeMs;
    uint8_t _current = NEUTRAL;

    size_t Tail() const { return (_head + _count - 1) % BUFFER_CAPACITY; }

    void Push(const DirectionSample& s) {
        size_t idx = (_head + _count) % BUFFER_CAPACITY;
        if (_count == BUFFER_CAPACITY) {
            _head = (_head + 1) % BUFFER_CAPACITY;  // drop oldest, capacity stays full
        } else {
            _count++;
        }
        _samples[idx] = s;
    }

    void Trim(uint32_t nowMs) {
        while (_count > 0 && (nowMs - _samples[_head].startedAtMs) > _maxAgeMs) {
            _head = (_head + 1) % BUFFER_CAPACITY;
            _count--;
        }
    }
};

// ---------------------------------------------------------------------------------------------
// MotionMatcher
// ---------------------------------------------------------------------------------------------

struct MotionDefinition {
    const char* name = "";
    uint8_t sequence[MAX_SEQUENCE_LEN] = {};
    uint8_t sequenceLen = 0;
    bool allowDiagonalSkip = true;
    uint32_t maxSequenceMs = 0;  // 0 = use MotionLeniencySettings::maxSequenceMs
    uint32_t maxGapMs = 0;       // 0 = use MotionLeniencySettings::maxGapMs
};

struct MotionLeniencySettings {
    uint32_t maxSequenceMs = 500;
    uint32_t maxGapMs = 250;
    uint32_t attackWindowMs = 300;
    uint32_t motionCooldownMs = 150;
};

struct MotionMatchResult {
    bool matched = false;
    const char* motionName = nullptr;
    uint8_t startDirection = NEUTRAL;
    uint8_t finalDirection = NEUTRAL;
    uint32_t startedAtMs = 0;
    uint32_t completedAtMs = 0;
};

// Scans a MotionBuffer for the configured motions, in priority (array) order. A motion is
// recognized the instant its final required step is the buffer's most-recent sample - detection
// is immediate, not polled after the fact. The final step always matches exactly; only earlier
// steps get diagonal-adjacency leniency (see MotionDefinition::allowDiagonalSkip).
class MotionMatcher {
public:
    MotionMatcher(const MotionDefinition* motions, size_t motionCount, const MotionLeniencySettings& leniency)
        : _motions(motions), _motionCount(motionCount), _leniency(leniency) {
        for (size_t i = 0; i < MAX_MOTIONS; i++) {
            _hasFired[i] = false;
            _lastFiredMs[i] = 0;
        }
    }

    // Call after every buffer change. Returns the highest-priority motion that just completed,
    // if any (result.matched == false otherwise).
    MotionMatchResult TryMatch(MotionBuffer& buffer, uint32_t nowMs) {
        for (size_t m = 0; m < _motionCount && m < MAX_MOTIONS; m++) {
            const MotionDefinition& motion = _motions[m];
            if (motion.sequenceLen == 0) {
                continue;
            }
            if (_hasFired[m] && (nowMs - _lastFiredMs[m]) < _leniency.motionCooldownMs) {
                continue;
            }

            uint32_t startedAt, completedAt;
            if (TryMatchSequence(buffer, motion, _leniency, nowMs, startedAt, completedAt)) {
                _hasFired[m] = true;
                _lastFiredMs[m] = nowMs;

                // Spent samples: leaving them in the buffer would let a returning/following
                // direction complete a different motion sharing a suffix (dp [6,2,3] -> back to 6
                // also reads as qcf's [2,3,6]).
                buffer.ConsumeHistory();

                MotionMatchResult result;
                result.matched = true;
                result.motionName = motion.name;
                result.startDirection = motion.sequence[0];
                result.finalDirection = motion.sequence[motion.sequenceLen - 1];
                result.startedAtMs = startedAt;
                result.completedAtMs = completedAt;
                return result;
            }
        }
        return MotionMatchResult{};
    }

    void Reset() {
        for (size_t i = 0; i < MAX_MOTIONS; i++) _hasFired[i] = false;
    }

private:
    const MotionDefinition* _motions;
    size_t _motionCount;
    MotionLeniencySettings _leniency;
    bool _hasFired[MAX_MOTIONS];
    uint32_t _lastFiredMs[MAX_MOTIONS];

    static bool Matches(const DirectionSample& sample, uint8_t required, bool allowDiagonal) {
        return sample.direction == required || (allowDiagonal && IsAdjacent(sample.direction, required));
    }

    static bool TryMatchSequence(MotionBuffer& buffer, const MotionDefinition& motion,
                                  const MotionLeniencySettings& leniency, uint32_t nowMs,
                                  uint32_t& startedAt, uint32_t& completedAt) {
        size_t sampleCount = buffer.Size();
        if (motion.sequenceLen == 0 || sampleCount == 0) {
            return false;
        }

        // Final direction always matches exactly - never via diagonal substitution - or motions
        // sharing directions in different orders (dp [6,2,3] vs qcf [2,3,6]) would bleed together.
        DirectionSample last = buffer.At(sampleCount - 1, nowMs);
        if (last.direction != motion.sequence[motion.sequenceLen - 1]) {
            return false;
        }

        completedAt = last.startedAtMs;
        uint32_t boundary = last.startedAtMs;
        startedAt = boundary;

        uint32_t maxGap = motion.maxGapMs != 0 ? motion.maxGapMs : leniency.maxGapMs;

        int si = (int)motion.sequenceLen - 2;
        long bi = (long)sampleCount - 2;

        while (si >= 0) {
            if (bi < 0) {
                return false;
            }

            DirectionSample sample = buffer.At((size_t)bi, nowMs);
            uint32_t gap = boundary - sample.startedAtMs;

            if (gap <= maxGap && Matches(sample, motion.sequence[si], motion.allowDiagonalSkip)) {
                startedAt = sample.startedAtMs;
                boundary = sample.startedAtMs;
                si--;
            } else if (gap > maxGap) {
                return false;
            }
            bi--;
        }

        uint32_t maxSequence = motion.maxSequenceMs != 0 ? motion.maxSequenceMs : leniency.maxSequenceMs;
        return (completedAt - startedAt) <= maxSequence;
    }
};

// ---------------------------------------------------------------------------------------------
// OutputResolver
// ---------------------------------------------------------------------------------------------

enum class OutputKind : uint8_t { ControllerButton, Key };

struct PrimitiveOutput {
    OutputKind kind = OutputKind::Key;
    char value[16] = {};
};

constexpr size_t MAX_RESOLVED_OUTPUTS = 8;

struct OutputList {
    PrimitiveOutput items[MAX_RESOLVED_OUTPUTS];
    size_t count = 0;

    void Add(OutputKind kind, const char* value) {
        if (count >= MAX_RESOLVED_OUTPUTS) return;
        items[count].kind = kind;
        std::strncpy(items[count].value, value, sizeof(items[count].value) - 1);
        items[count].value[sizeof(items[count].value) - 1] = '\0';
        count++;
    }
};

// Data available while resolving placeholder tokens ($controller_motion_final, $attack, ...)
// into primitive outputs.
struct OutputContext {
    bool hasStartDirection = false;
    uint8_t startDirection = NEUTRAL;
    bool hasFinalDirection = false;
    uint8_t finalDirection = NEUTRAL;
    bool hasAttackButton = false;
    char attackButton[16] = {};
};

namespace OutputResolver {

// Writes up to 2 dpad button names for a numpad direction into `out`, returns how many.
inline size_t DirectionButtons(uint8_t numpad, const char* out[2]) {
    switch (numpad) {
        case 7: out[0] = "dpad_up";   out[1] = "dpad_left";  return 2;
        case 8: out[0] = "dpad_up";                          return 1;
        case 9: out[0] = "dpad_up";   out[1] = "dpad_right"; return 2;
        case 4: out[0] = "dpad_left";                        return 1;
        case 6: out[0] = "dpad_right";                       return 1;
        case 1: out[0] = "dpad_down"; out[1] = "dpad_left";  return 2;
        case 2: out[0] = "dpad_down";                        return 1;
        case 3: out[0] = "dpad_down"; out[1] = "dpad_right"; return 2;
        default: return 0;
    }
}

inline void ResolveToken(const char* token, const OutputContext& ctx, OutputList& result) {
    static constexpr char CTRL_PREFIX[] = "controller:";
    static constexpr char DIR_PREFIX[] = "controller_direction:";
    constexpr size_t CTRL_PREFIX_LEN = sizeof(CTRL_PREFIX) - 1;
    constexpr size_t DIR_PREFIX_LEN = sizeof(DIR_PREFIX) - 1;

    if (std::strncmp(token, CTRL_PREFIX, CTRL_PREFIX_LEN) == 0) {
        result.Add(OutputKind::ControllerButton, token + CTRL_PREFIX_LEN);
        return;
    }

    if (std::strncmp(token, DIR_PREFIX, DIR_PREFIX_LEN) == 0) {
        uint8_t numpad = (uint8_t)std::atoi(token + DIR_PREFIX_LEN);
        const char* btns[2];
        size_t n = DirectionButtons(numpad, btns);
        for (size_t i = 0; i < n; i++) result.Add(OutputKind::ControllerButton, btns[i]);
        return;
    }

    if (std::strcmp(token, "$controller_motion_final") == 0) {
        if (ctx.hasFinalDirection) {
            const char* btns[2];
            size_t n = DirectionButtons(ctx.finalDirection, btns);
            for (size_t i = 0; i < n; i++) result.Add(OutputKind::ControllerButton, btns[i]);
        }
        return;
    }

    if (std::strcmp(token, "$controller_motion_start") == 0) {
        if (ctx.hasStartDirection) {
            const char* btns[2];
            size_t n = DirectionButtons(ctx.startDirection, btns);
            for (size_t i = 0; i < n; i++) result.Add(OutputKind::ControllerButton, btns[i]);
        }
        return;
    }

    if (std::strcmp(token, "$attack") == 0) {
        if (ctx.hasAttackButton) {
            result.Add(OutputKind::ControllerButton, ctx.attackButton);
        }
        return;
    }

    result.Add(OutputKind::Key, token);
}

inline void Resolve(const char* const* tokens, size_t tokenCount, const OutputContext& ctx, OutputList& result) {
    for (size_t i = 0; i < tokenCount; i++) {
        ResolveToken(tokens[i], ctx, result);
    }
}

}  // namespace OutputResolver

}  // namespace MotionInput

// ---------------------------------------------------------------------------------------------
// Integration notes (GP2040-CE)
// ---------------------------------------------------------------------------------------------
// 1. Timestamps: pass to_ms_since_boot(get_absolute_time()) (Pico SDK) as `nowMs` everywhere.
//    All comparisons are unsigned-subtraction based, so the ~49-day millis rollover is safe.
//
// 2. Wiring per tick, inside a GP2040-CE addon's `process()` (runs once per USB report cycle):
//        DirectionInputs in{ gamepad->pressedUp(), gamepad->pressedDown(),
//                             gamepad->pressedLeft(), gamepad->pressedRight() };
//        uint8_t dir = MotionInput::MapDirection(in);
//        if (buffer.Update(dir, nowMs)) {
//            auto match = matcher.TryMatch(buffer, nowMs);
//            if (match.matched) { /* start an attack-window watch, as MotionInputEngine::Tick does */ }
//        }
//
// 3. Output tokens: this file keeps OutputResolver string-token-based for a faithful 1:1 port of
//    OutputResolver.cs. On-device, prefer precomputing each profile's tokens into OutputList once
//    at profile-load time (not every tick) - token parsing has no business running in the hot
//    loop on an RP2040. The struct split (OutputList holds already-resolved PrimitiveOutputs)
//    supports that: resolve once when the profile loads or a motion fires, cache the OutputList.
//
