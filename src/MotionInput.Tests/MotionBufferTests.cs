using MotionInput.Core.Motion;

namespace MotionInput.Tests;

public class MotionBufferTests
{
    [Fact]
    public void Update_returns_false_when_direction_unchanged()
    {
        var buffer = new MotionBuffer();
        var t0 = DateTime.UtcNow;
        Assert.True(buffer.Update(6, t0));
        Assert.False(buffer.Update(6, t0.AddMilliseconds(10)));
    }

    [Fact]
    public void Neutral_is_not_recorded_by_default()
    {
        var buffer = new MotionBuffer();
        var t0 = DateTime.UtcNow;
        buffer.Update(6, t0);
        buffer.Update(5, t0.AddMilliseconds(10));
        buffer.Update(4, t0.AddMilliseconds(20));

        var snapshot = buffer.Snapshot(t0.AddMilliseconds(30));
        Assert.Equal(new[] { 6, 4 }, snapshot.Select(s => s.Direction));
    }

    [Fact]
    public void Snapshot_closes_the_open_sample_at_the_given_time()
    {
        var buffer = new MotionBuffer();
        var t0 = DateTime.UtcNow;
        buffer.Update(6, t0);

        var asOf = t0.AddMilliseconds(50);
        var snapshot = buffer.Snapshot(asOf);

        Assert.Single(snapshot);
        Assert.Equal(asOf, snapshot[0].EndedAt);
    }

    [Fact]
    public void Old_samples_are_trimmed_past_max_age()
    {
        var buffer = new MotionBuffer(maxAge: TimeSpan.FromMilliseconds(100));
        var t0 = DateTime.UtcNow;
        buffer.Update(2, t0);
        buffer.Update(6, t0.AddMilliseconds(50));

        var snapshot = buffer.Snapshot(t0.AddMilliseconds(250));
        Assert.DoesNotContain(snapshot, s => s.Direction == 2);
    }

    [Fact]
    public void Sequence_of_direction_changes_is_recorded_in_order()
    {
        var buffer = new MotionBuffer();
        var t0 = DateTime.UtcNow;
        buffer.Update(2, t0);
        buffer.Update(3, t0.AddMilliseconds(50));
        buffer.Update(6, t0.AddMilliseconds(100));

        var snapshot = buffer.Snapshot(t0.AddMilliseconds(150));
        Assert.Equal(new[] { 2, 3, 6 }, snapshot.Select(s => s.Direction));
    }
}
