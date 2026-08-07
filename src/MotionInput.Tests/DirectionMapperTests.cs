using MotionInput.Core.Input;
using MotionInput.Core.Models;

namespace MotionInput.Tests;

public class DirectionMapperTests
{
    private static ControllerInputSettings DpadOnly => new() { DirectionSources = new() { "dpad" } };
    private static ControllerInputSettings LeftStickOnly => new() { DirectionSources = new() { "left_stick" }, StickDeadzone = 0.3 };

    private static ControllerSnapshot Digital(params string[] held) =>
        new(DateTime.UtcNow, held.ToHashSet(), new Dictionary<string, float>());

    private static ControllerSnapshot Analog(string xId, float x, string yId, float y) =>
        new(DateTime.UtcNow, new HashSet<string>(), new Dictionary<string, float> { [xId] = x, [yId] = y });

    [Theory]
    [InlineData(new[] { "dpad_up" }, 8)]
    [InlineData(new[] { "dpad_down" }, 2)]
    [InlineData(new[] { "dpad_left" }, 4)]
    [InlineData(new[] { "dpad_right" }, 6)]
    [InlineData(new[] { "dpad_up", "dpad_left" }, 7)]
    [InlineData(new[] { "dpad_up", "dpad_right" }, 9)]
    [InlineData(new[] { "dpad_down", "dpad_left" }, 1)]
    [InlineData(new[] { "dpad_down", "dpad_right" }, 3)]
    [InlineData(new string[0], 5)]
    public void Maps_dpad_to_numpad(string[] held, int expected)
    {
        Assert.Equal(expected, DirectionMapper.Map(Digital(held), DpadOnly));
    }

    [Fact]
    public void Opposite_dpad_directions_cancel_to_neutral()
    {
        Assert.Equal(5, DirectionMapper.Map(Digital("dpad_left", "dpad_right"), DpadOnly));
        Assert.Equal(5, DirectionMapper.Map(Digital("dpad_up", "dpad_down"), DpadOnly));
    }

    [Fact]
    public void Stick_below_deadzone_is_neutral()
    {
        var snapshot = Analog("leftstick_x", 0.1f, "leftstick_y", 0.1f);
        Assert.Equal(5, DirectionMapper.Map(snapshot, LeftStickOnly));
    }

    [Fact]
    public void Stick_past_deadzone_resolves_direction()
    {
        var snapshot = Analog("leftstick_x", 0.9f, "leftstick_y", 0.9f);
        Assert.Equal(9, DirectionMapper.Map(snapshot, LeftStickOnly));
    }

    [Theory]
    [InlineData(2, 3, true)]
    [InlineData(2, 6, false)]
    [InlineData(2, 1, true)]
    [InlineData(6, 6, true)]
    [InlineData(8, 2, false)]
    public void IsAdjacent_matches_expected_pairs(int a, int b, bool expected)
    {
        Assert.Equal(expected, DirectionMapper.IsAdjacent(a, b));
    }
}
