using MotionInput.Core.Engine;
using MotionInput.Core.Models;
using MotionInput.Core.Profiles;
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

    // Regression test: after a motion+attack combo fires (e.g. qcf+M -> a forced d-pad direction
    // plus a literal attack button, the same shape real profiles use — not the "$attack" token),
    // the combo used to be a fully one-shot ~50ms pulse. If the player kept M held down, the
    // combo's button should stay held on the virtual pad for as long as M is, same as a plain
    // (non-combo) attack would — and its d-pad override should stay put too, not flicker against
    // whatever direction is physically held underneath it.

    [Fact]
    public void Holding_the_attack_after_a_motion_combo_fires_keeps_the_combo_outputs_held()
    {
        var profile = MakeProfile();
        profile.Motions.Add(new() { Name = "qcf", Sequence = new() { 2, 3, 6 } });
        profile.MotionAttackOutputs["qcf"] = new()
        {
            ["light"] = new() { "controller_direction:6", "controller:a" },
        };

        var source = new FakeControllerSource();
        var gamepad = new FakeVirtualGamepad();
        using var engine = new MotionInputEngine(profile, source, gamepad);

        engine.Start();
        try
        {
            // Roll through all three required directions distinctly (2, then 3, then 6) rather than
            // jumping straight to a diagonal — diagonal-adjacency leniency lets a nearby sample
            // *substitute* for a required step, it doesn't let one sample satisfy two steps at once.
            source.SetHeld("dpad_down");
            Thread.Sleep(30);
            source.SetHeld("dpad_down", "dpad_right");
            Thread.Sleep(30);
            source.SetHeld("dpad_right");
            Thread.Sleep(30);
            source.SetHeld("dpad_right", "x"); // qcf completes, attack lands inside the window

            Thread.Sleep(200); // well past the old 50ms macro pulse duration
            Assert.True(gamepad.IsHeld("a"));
            Assert.True(gamepad.IsHeld("dpad_right")); // combo's own d-pad override stays put

            source.SetHeld(); // release
            Thread.Sleep(100);
            Assert.False(gamepad.IsHeld("a"));
            Assert.False(gamepad.IsHeld("dpad_right"));
        }
        finally
        {
            engine.Stop();
        }
    }

    // Regression test for the role-name output notation ("236 + Light = 5 + S1"): a combo should be
    // able to name a completely different role's bound button as its output, resolved through that
    // role's own AttackOutputs (not the raw physical button it was captured on), and "5" should force
    // the d-pad to neutral (no press) rather than mirroring whatever direction is still held.

    [Fact]
    public void Motion_combo_output_can_name_another_role_and_force_neutral_direction()
    {
        var profile = MakeProfile();
        profile.AttackBindings["s1"] = new() { "rb" };
        profile.AttackOutputs["s1"] = new() { "controller:lb" }; // s1 is remapped, not a plain passthrough
        profile.Motions.Add(new() { Name = "qcf", Sequence = new() { 2, 3, 6 } });
        profile.MotionAttackOutputs["qcf"] = new()
        {
            ["light"] = new() { "5", "s1" },
        };

        var source = new FakeControllerSource();
        var gamepad = new FakeVirtualGamepad();
        using var engine = new MotionInputEngine(profile, source, gamepad);

        engine.Start();
        try
        {
            source.SetHeld("dpad_down");
            Thread.Sleep(30);
            source.SetHeld("dpad_down", "dpad_right");
            Thread.Sleep(30);
            source.SetHeld("dpad_right");
            Thread.Sleep(30);
            source.SetHeld("dpad_right", "x"); // qcf completes, light lands inside the window

            Thread.Sleep(200);
            Assert.True(gamepad.IsHeld("lb")); // "s1" resolved through s1's own AttackOutputs, not "rb"
            Assert.False(gamepad.IsHeld("dpad_up"));
            Assert.False(gamepad.IsHeld("dpad_down"));
            Assert.False(gamepad.IsHeld("dpad_left"));
            Assert.False(gamepad.IsHeld("dpad_right")); // "5" forced neutral, overriding the held direction

            source.SetHeld();
            Thread.Sleep(100);
            Assert.False(gamepad.IsHeld("lb"));
        }
        finally
        {
            engine.Stop();
        }
    }

    // Regression test for the shipped default profile's "direction,role" combo notation (e.g. qcf's
    // light entry is literally {"6", "S1"}): once S1 is bound the way the Bindings tab would bind it
    // (AttackBindings + AttackOutputs, same as any other role), the dp special should actually fire.

    [Fact]
    public void Default_profile_dp_light_combo_fires_through_the_S1_role_once_bound()
    {
        var profile = ProfileStore.CreateDefault();
        profile.AttackBindings["s1"] = new() { "rb" };
        profile.AttackOutputs["s1"] = new() { "controller:rb" };

        var source = new FakeControllerSource();
        var gamepad = new FakeVirtualGamepad();
        using var engine = new MotionInputEngine(profile, source, gamepad);

        engine.Start();
        try
        {
            // dp = [6, 2, 3]
            source.SetHeld("dpad_right");
            Thread.Sleep(30);
            source.SetHeld("dpad_down");
            Thread.Sleep(30);
            source.SetHeld("dpad_down", "dpad_right");
            Thread.Sleep(30);
            source.SetHeld("dpad_down", "dpad_right", "x"); // dp completes, light lands inside the window

            Thread.Sleep(200);
            Assert.True(gamepad.IsHeld("rb")); // "S1" resolved through s1's AttackOutputs
            Assert.True(gamepad.IsHeld("dpad_right")); // dp light's "6" forced direction

            source.SetHeld();
            Thread.Sleep(100);
            Assert.False(gamepad.IsHeld("rb"));
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
