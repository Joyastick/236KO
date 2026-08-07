using MotionInput.Core.Motion;

namespace MotionInput.Tests;

public class MotionMatcherTests
{
    private static MotionDefinition Qcf => new() { Name = "qcf", Sequence = new() { 2, 3, 6 } };
    private static MotionLeniencySettings DefaultLeniency => new() { MaxSequenceMs = 500, MaxGapMs = 250, MotionCooldownMs = 150 };

    [Fact]
    public void Recognizes_exact_sequence_the_instant_it_completes()
    {
        var buffer = new MotionBuffer();
        var matcher = new MotionMatcher(new[] { Qcf }, DefaultLeniency);
        var t0 = DateTime.UtcNow;

        buffer.Update(2, t0);
        Assert.Null(matcher.TryMatch(buffer, t0));

        buffer.Update(3, t0.AddMilliseconds(50));
        Assert.Null(matcher.TryMatch(buffer, t0.AddMilliseconds(50)));

        buffer.Update(6, t0.AddMilliseconds(100));
        var result = matcher.TryMatch(buffer, t0.AddMilliseconds(100));

        Assert.NotNull(result);
        Assert.Equal("qcf", result!.MotionName);
        Assert.Equal(2, result.StartDirection);
        Assert.Equal(6, result.FinalDirection);
    }

    [Fact]
    public void Does_not_match_until_the_final_direction_is_current()
    {
        var buffer = new MotionBuffer();
        var matcher = new MotionMatcher(new[] { Qcf }, DefaultLeniency);
        var t0 = DateTime.UtcNow;

        buffer.Update(2, t0);
        buffer.Update(3, t0.AddMilliseconds(50));

        // Buffer's most recent sample is still "3", not the required final "6" — must not match yet.
        Assert.Null(matcher.TryMatch(buffer, t0.AddMilliseconds(60)));
    }

    [Fact]
    public void Fails_when_gap_between_steps_exceeds_max_gap()
    {
        var buffer = new MotionBuffer();
        var leniency = DefaultLeniency with { MaxGapMs = 100 };
        var matcher = new MotionMatcher(new[] { Qcf }, leniency);
        var t0 = DateTime.UtcNow;

        buffer.Update(2, t0);
        buffer.Update(3, t0.AddMilliseconds(50));
        buffer.Update(6, t0.AddMilliseconds(300)); // 250ms gap since "3" > 100ms max gap

        Assert.Null(matcher.TryMatch(buffer, t0.AddMilliseconds(300)));
    }

    [Fact]
    public void Diagonal_adjacent_direction_satisfies_a_required_step()
    {
        var buffer = new MotionBuffer();
        var matcher = new MotionMatcher(new[] { Qcf }, DefaultLeniency);
        var t0 = DateTime.UtcNow;

        // Player rolled straight through 1 (down-left) instead of hitting 2 (down) cleanly;
        // 1 is ring-adjacent to 2, so it should still satisfy the "2" step.
        buffer.Update(1, t0);
        buffer.Update(3, t0.AddMilliseconds(50));
        buffer.Update(6, t0.AddMilliseconds(100));

        var result = matcher.TryMatch(buffer, t0.AddMilliseconds(100));
        Assert.NotNull(result);
    }

    [Fact]
    public void Cooldown_prevents_immediate_retrigger()
    {
        var buffer = new MotionBuffer();
        var matcher = new MotionMatcher(new[] { Qcf }, DefaultLeniency);
        var t0 = DateTime.UtcNow;

        buffer.Update(2, t0);
        buffer.Update(3, t0.AddMilliseconds(50));
        buffer.Update(6, t0.AddMilliseconds(100));
        Assert.NotNull(matcher.TryMatch(buffer, t0.AddMilliseconds(100)));

        // Same completed direction still current; re-checking immediately should be suppressed by cooldown.
        Assert.Null(matcher.TryMatch(buffer, t0.AddMilliseconds(120)));
    }

    [Fact]
    public void Higher_priority_motion_wins_when_both_could_match()
    {
        var dp = new MotionDefinition { Name = "dp", Sequence = new() { 6, 2, 3 } };
        var qcf = new MotionDefinition { Name = "qcf", Sequence = new() { 2, 3, 6 } };
        var buffer = new MotionBuffer();

        // dp listed first: with samples [6,2,3] only dp completes (qcf's final direction would need to be 6).
        var matcher = new MotionMatcher(new[] { dp, qcf }, DefaultLeniency);
        var t0 = DateTime.UtcNow;

        buffer.Update(6, t0);
        buffer.Update(2, t0.AddMilliseconds(50));
        buffer.Update(3, t0.AddMilliseconds(100));

        var result = matcher.TryMatch(buffer, t0.AddMilliseconds(100));
        Assert.Equal("dp", result!.MotionName);
    }
}
