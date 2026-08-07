namespace MotionInput.Core.Motion;

/// <summary>A numpad direction that was held, with when it started and (if known) when it ended.</summary>
public readonly record struct DirectionSample(int Direction, DateTime StartedAt, DateTime? EndedAt)
{
    public bool IsOngoing => EndedAt is null;
}
