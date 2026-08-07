namespace MotionInput.Core.Motion;

/// <summary>
/// Rolling history of numpad direction changes. Only transitions are recorded (not every poll
/// tick), so the buffer represents "what directions were pressed, and in what order" which is
/// what motion matching needs. A max age keeps very old input from lingering forever.
/// </summary>
public sealed class MotionBuffer
{
    private readonly List<DirectionSample> _samples = new();
    private readonly int _capacity;
    private readonly TimeSpan _maxAge;
    private int _current = Neutral;

    private const int Neutral = 5;

    public MotionBuffer(int capacity = 32, TimeSpan? maxAge = null)
    {
        _capacity = capacity;
        _maxAge = maxAge ?? TimeSpan.FromSeconds(2);
    }

    public int Current => _current;

    /// <summary>Feed the latest resolved numpad direction. Returns true if this was a change (i.e. a new sample was recorded).</summary>
    public bool Update(int direction, DateTime timestamp, bool recordNeutral = false)
    {
        if (direction == _current)
        {
            return false;
        }

        if (_samples.Count > 0 && _samples[^1].EndedAt is null)
        {
            _samples[^1] = _samples[^1] with { EndedAt = timestamp };
        }

        _current = direction;

        if (direction != Neutral || recordNeutral)
        {
            _samples.Add(new DirectionSample(direction, timestamp, null));
            while (_samples.Count > _capacity)
            {
                _samples.RemoveAt(0);
            }
        }

        Trim(timestamp);
        return true;
    }

    private void Trim(DateTime now)
    {
        while (_samples.Count > 0 && now - _samples[0].StartedAt > _maxAge)
        {
            _samples.RemoveAt(0);
        }
    }

    /// <summary>Snapshot of the buffer, oldest first, with any still-open sample closed off at <paramref name="asOf"/>.</summary>
    public IReadOnlyList<DirectionSample> Snapshot(DateTime asOf)
    {
        var result = new List<DirectionSample>(_samples.Count);
        foreach (var s in _samples)
        {
            if (asOf - s.StartedAt > _maxAge) continue;
            result.Add(s.EndedAt is null ? s with { EndedAt = asOf } : s);
        }
        return result;
    }

    public void Clear()
    {
        _samples.Clear();
        _current = Neutral;
    }
}
