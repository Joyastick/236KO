using MotionInput.Core.Input;

namespace MotionInput.Core.Motion;

/// <summary>
/// Scans a <see cref="MotionBuffer"/> for the profile's motion definitions, in priority order
/// (the order they're defined). A motion is recognized the instant its final required step lands
/// as the buffer's most recent sample, so detection is immediate rather than polled after the fact.
/// The final step must match exactly (see <see cref="MotionDefinition.AllowDiagonalSkip"/>); only
/// earlier steps get diagonal-adjacency leniency.
/// </summary>
public sealed class MotionMatcher
{
    private readonly List<MotionDefinition> _motions;
    private readonly MotionLeniencySettings _leniency;
    private readonly Dictionary<string, DateTime> _lastFired = new();

    public MotionMatcher(IEnumerable<MotionDefinition> motions, MotionLeniencySettings leniency)
    {
        _motions = motions.ToList();
        _leniency = leniency;
    }

    /// <summary>Call after every buffer change. Returns the highest-priority motion that just completed, if any.</summary>
    public MotionMatchResult? TryMatch(MotionBuffer buffer, DateTime now)
    {
        var samples = buffer.Snapshot(now);

        foreach (var motion in _motions)
        {
            if (motion.Sequence.Count == 0)
            {
                continue;
            }

            if (_lastFired.TryGetValue(motion.Name, out var last) &&
                now - last < TimeSpan.FromMilliseconds(_leniency.MotionCooldownMs))
            {
                continue;
            }

            if (TryMatchSequence(samples, motion, _leniency, out var startedAt, out var completedAt))
            {
                _lastFired[motion.Name] = now;
                return new MotionMatchResult(motion.Name, motion.Sequence[0], motion.Sequence[^1], startedAt, completedAt);
            }
        }

        return null;
    }

    public void Reset() => _lastFired.Clear();

    private static bool Matches(DirectionSample sample, int required, bool allowDiagonal) =>
        sample.Direction == required || (allowDiagonal && DirectionMapper.IsAdjacent(sample.Direction, required));

    private static bool TryMatchSequence(
        IReadOnlyList<DirectionSample> samples,
        MotionDefinition motion,
        MotionLeniencySettings global,
        out DateTime startedAt,
        out DateTime completedAt)
    {
        startedAt = default;
        completedAt = default;

        var seq = motion.Sequence;
        if (seq.Count == 0 || samples.Count == 0)
        {
            return false;
        }

        // The final direction is always matched exactly, never via diagonal-adjacency substitution.
        // Adjacency leniency is only for the roll leading up to it; if it applied here too, motions
        // that share directions in different orders (e.g. dp [6,2,3] and qcf [2,3,6]) would bleed
        // into each other — a qcf ending held on 6 would satisfy dp's "~3" final step, since 6 and 3
        // are ring-adjacent, and fire dp instead of (or as well as) qcf.
        var bi = samples.Count - 1;
        if (samples[bi].Direction != seq[^1])
        {
            return false;
        }

        completedAt = samples[bi].StartedAt;
        var boundary = samples[bi].StartedAt;
        startedAt = boundary;

        var maxGap = TimeSpan.FromMilliseconds(motion.MaxGapMs ?? global.MaxGapMs);
        var si = seq.Count - 2;
        bi--;

        while (si >= 0)
        {
            if (bi < 0)
            {
                return false;
            }

            var sample = samples[bi];
            var gap = boundary - sample.StartedAt;

            if (gap <= maxGap && Matches(sample, seq[si], motion.AllowDiagonalSkip))
            {
                startedAt = sample.StartedAt;
                boundary = sample.StartedAt;
                si--;
            }
            else if (gap > maxGap)
            {
                return false;
            }

            bi--;
        }

        var maxSequence = TimeSpan.FromMilliseconds(motion.MaxSequenceMs ?? global.MaxSequenceMs);
        return completedAt - startedAt <= maxSequence;
    }
}
