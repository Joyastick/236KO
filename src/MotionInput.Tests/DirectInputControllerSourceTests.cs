using MotionInput.Core.Input;

namespace MotionInput.Tests;

public class DirectInputControllerSourceTests
{
    // Regression test: a hat switch's exact diagonal positions (4500, 13500, 22500, 31500 -
    // hundredths of a degree) were previously resolving to only one of their two neighboring
    // cardinals instead of both, due to a mismatched inclusive/exclusive bucket boundary. That
    // silently dropped the diagonal step out of the motion buffer for anyone using a DirectInput
    // pad's hat switch, breaking every diagonal-involving motion (qcf, qcb, dp, rdp, half-circles)
    // while the live Held Inputs/Direction readout still looked correct at each instant.

    [Theory]
    [InlineData(0, new[] { "dpad_up" })]
    [InlineData(4500, new[] { "dpad_up", "dpad_right" })]
    [InlineData(9000, new[] { "dpad_right" })]
    [InlineData(13500, new[] { "dpad_right", "dpad_down" })]
    [InlineData(18000, new[] { "dpad_down" })]
    [InlineData(22500, new[] { "dpad_down", "dpad_left" })]
    [InlineData(27000, new[] { "dpad_left" })]
    [InlineData(31500, new[] { "dpad_left", "dpad_up" })]
    public void HatAngleToDpadIds_maps_every_8_way_position_correctly(int pov, string[] expected)
    {
        var actual = DirectInputControllerSource.HatAngleToDpadIds(pov).ToList();
        Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
    }

    [Fact]
    public void HatAngleToDpadIds_returns_nothing_for_a_centered_or_absent_hat()
    {
        Assert.Empty(DirectInputControllerSource.HatAngleToDpadIds(-1));
    }
}
