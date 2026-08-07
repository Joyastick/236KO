namespace MotionInput.Core.Motion;

public sealed record MotionMatchResult(string MotionName, int StartDirection, int FinalDirection, DateTime StartedAt, DateTime CompletedAt);
