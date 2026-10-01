using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

public sealed class FreeCamInputTests
{
    [Fact]
    public void Keys_map_to_move_roll_and_modifiers()
    {
        var h = new FakeShieldHandle { Mods = ModifierKeys.Shift, Rmb = true, Delta = (4f, -2f), WheelValue = 1f };
        h.Held.UnionWith(new[] { StellarKeyCode.W, StellarKeyCode.D, StellarKeyCode.E, StellarKeyCode.C });
        var (i, _) = new FreeCamInput().Read(h);
        Assert.Equal(new System.Numerics.Vector3(1, 1, 1), i.Move);
        Assert.Equal(1f, i.RollAxis);
        Assert.True(i.Shift);
        Assert.True(i.Looking);
        Assert.Equal(4f, i.LookX);
        Assert.Equal(-2f, i.LookY);
        Assert.Equal(1f, i.Wheel);
    }

    [Fact]
    public void Edges_fire_once_per_press()
    {
        var input = new FreeCamInput();
        var h = new FakeShieldHandle();
        h.Held.Add(StellarKeyCode.Space);
        Assert.True(input.Read(h).Edges.ToggleFreeze);
        Assert.False(input.Read(h).Edges.ToggleFreeze);
        h.Held.Clear();
        input.Read(h);
        h.Held.Add(StellarKeyCode.Space);
        Assert.True(input.Read(h).Edges.ToggleFreeze);
    }

    [Fact]
    public void A_key_already_held_at_entry_does_not_fire()
    {
        var input = new FreeCamInput();
        var h = new FakeShieldHandle();
        h.Held.Add(StellarKeyCode.Tab);
        input.Prime(h);
        Assert.False(input.Read(h).Edges.ToggleMode);
    }

    [Fact]
    public void Alt_tab_is_not_a_mode_switch()
    {
        var h = new FakeShieldHandle { Mods = ModifierKeys.Alt };
        h.Held.Add(StellarKeyCode.Tab);
        Assert.False(new FreeCamInput().Read(h).Edges.ToggleMode);
    }

    [Fact]
    public void Left_click_reports_the_pointer_once()
    {
        var input = new FreeCamInput();
        var h = new FakeShieldHandle { Lmb = true, PointerAt = (10f, 20f) };
        Assert.Equal((10f, 20f), input.Read(h).Edges.Click);
        Assert.Null(input.Read(h).Edges.Click);
    }

    [Fact]
    public void Escape_is_an_exit_edge_and_an_Escape_held_at_entry_is_ignored()   // spec D8 (owner 2026-10-01)
    {
        var input = new FreeCamInput();
        var h = new FakeShieldHandle();
        h.Held.Add(StellarKeyCode.Escape);
        input.Prime(h);
        Assert.False(input.Read(h).Edges.Exit);
        h.Held.Clear();
        input.Read(h);
        h.Held.Add(StellarKeyCode.Escape);
        Assert.True(input.Read(h).Edges.Exit);
        Assert.False(input.Read(h).Edges.Exit);
    }
}
