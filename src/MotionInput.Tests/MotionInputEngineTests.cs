using MotionInput.Core.Engine;
using MotionInput.Core.Models;
using MotionInput.Tests.Fakes;

namespace MotionInput.Tests;

public class MotionInputEngineTests
{
    private static Profile MakeProfile()
    {
        var profile = new Profile { Name = "Test" };
        profile.ControllerInput.PollRateHz = 200;
        profile.KeyOutputs["start"] = new() { "controller:start" };
        profile.AttackBindings["light"] = new() { "x" };
        profile.AttackOutputs["light"] = new() { "controller:x" };
        return profile;
    }

    // Regression test: holding a physical button used to only pulse the mapped virtual button for
    // ~50ms (via the one-shot macro dispatcher) no matter how long it was held, instead of holding
    // the virtual button for as long as the physical one was held.

    [Fact]
    public void Holding_a_passthrough_key_output_keeps_the_virtual_button_held()
    {
        var profile = MakeProfile();
        var source = new FakeControllerSource();
        var gamepad = new FakeVirtualGamepad();
        using var engine = new MotionInputEngine(profile, source, gamepad);

        source.SetHeld("start");
        engine.Start();
        try
        {
            Thread.Sleep(200); // well past the old 50ms macro pulse duration
            Assert.True(gamepad.IsHeld("start"));

            source.SetHeld(); // release
            Thread.Sleep(100);
            Assert.False(gamepad.IsHeld("start"));
        }
        finally
        {
            engine.Stop();
        }
    }

    [Fact]
    public void Holding_a_bare_attack_button_keeps_the_virtual_button_held()
    {
        var profile = MakeProfile();
        var source = new FakeControllerSource();
        var gamepad = new FakeVirtualGamepad();
        using var engine = new MotionInputEngine(profile, source, gamepad);

        source.SetHeld("x");
        engine.Start();
        try
        {
            Thread.Sleep(200);
            Assert.True(gamepad.IsHeld("x"));

            source.SetHeld();
            Thread.Sleep(100);
            Assert.False(gamepad.IsHeld("x"));
        }
        finally
        {
            engine.Stop();
        }
    }
}
