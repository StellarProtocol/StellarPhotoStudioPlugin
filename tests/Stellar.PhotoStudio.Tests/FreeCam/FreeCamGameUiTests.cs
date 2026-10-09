using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

/// <summary>Game UI in the free camera (player request "display game ui on freecam so I can do my sliders", owner-approved
/// 2026-10-10): U / the Camera tab switch shows the game HUD for this session only, the scene keeps the FULL entry hide,
/// and the game's interface takes the pointer only while it is shown.</summary>
public sealed class FreeCamGameUiTests
{
    private const VisibilityLayers Hud = VisibilityLayers.GameHud;
    private const VisibilityLayers Names = VisibilityLayers.Nameplates;

    [Fact]
    public void U_shows_the_game_HUD_and_U_again_hides_it_other_entry_hides_untouched()
    {
        var r = new SessionRig();
        r.Session.Enter();
        Assert.Equal(Hud | Names, r.Visibility.Held());
        r.Press(StellarKeyCode.U);
        Assert.True(r.Session.GameUiShown);
        Assert.Equal(Names, r.Visibility.Held());
        r.Press(StellarKeyCode.U);
        Assert.False(r.Session.GameUiShown);
        Assert.Equal(Hud | Names, r.Visibility.Held());
    }

    [Fact]
    public void The_switch_does_nothing_while_the_free_camera_is_off()
    {
        var r = new SessionRig();
        r.Session.SetGameUi(true);
        Assert.False(r.Session.GameUiShown);
        Assert.Empty(r.Visibility.Hides);
    }

    // Spec § "After the free camera": leaving with a scene set gives the scene the FULL entry hide (HUD hidden again).
    [Fact]
    public void Leaving_with_a_scene_set_hands_the_full_entry_hide_to_the_scene()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Press(StellarKeyCode.Space);   // freeze → the scene keeps the entry hide
        r.Press(StellarKeyCode.U);
        r.Session.Exit();
        Assert.False(r.Session.GameUiShown);
        Assert.True(r.Scene.HoldsEntryHide);
        Assert.Equal(Hud | Names, r.Visibility.Held());
    }

    // Decision B (session only): every entry starts with the game UI hidden as configured.
    [Fact]
    public void Re_entry_starts_with_the_game_UI_hidden()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Press(StellarKeyCode.U);
        r.Session.Exit();
        Assert.Equal(VisibilityLayers.None, r.Visibility.Held());
        r.Session.Enter();
        Assert.False(r.Session.GameUiShown);
        Assert.Equal(Hud | Names, r.Visibility.Held());
    }

    [Fact]
    public void With_only_the_HUD_hidden_on_entry_U_drops_and_restores_that_one_hide()
    {
        var r = new SessionRig();
        r.Settings.SetEntryHide(Names, false);
        r.Session.Enter();
        r.Press(StellarKeyCode.U);
        Assert.Equal(VisibilityLayers.None, r.Visibility.Held());
        r.Press(StellarKeyCode.U);
        Assert.Equal(Hud, r.Visibility.Held());
    }

    [Fact]
    public void With_no_HUD_hide_on_entry_U_changes_no_hide()
    {
        var r = new SessionRig();
        r.Settings.SetEntryHide(Hud, false);
        r.Session.Enter();
        var before = r.Visibility.Hides.Count;
        r.Press(StellarKeyCode.U);
        Assert.True(r.Session.GameUiShown);
        Assert.Equal(before, r.Visibility.Hides.Count);
        Assert.Equal(Names, r.Visibility.Held());
    }

    // PINNED regression (found while building this feature — never weaken): an entry with NO entry hides left the
    // previous session's layers in the session, so U re-hid layers the player had just turned off.
    [Fact]
    public void An_entry_without_entry_hides_forgets_the_previous_sessions_layers()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Session.Exit();
        r.Settings.SetEntryHide(Hud, false);
        r.Settings.SetEntryHide(Names, false);
        r.Session.Enter();
        var before = r.Visibility.Hides.Count;
        r.Press(StellarKeyCode.U);
        r.Press(StellarKeyCode.U);
        Assert.Equal(before, r.Visibility.Hides.Count);
        Assert.Equal(VisibilityLayers.None, r.Visibility.Held());
    }

    [Fact]
    public void A_click_over_the_game_UI_picks_nobody_only_while_the_game_UI_is_shown()
    {
        var r = new SessionRig();
        r.Transforms.Positions[7] = new Position3D(3, 0, 3);
        r.Picker.Result = new EntityId(7);
        r.Shield.OverGameUi = true;   // a hidden game HUD may still answer the raycast — ignored while hidden
        r.Session.Enter();
        Click(r);
        Assert.Equal(new EntityId(7), r.Session.Subject);
        r.Session.SetSubject(r.Snapshot.LocalEntityId);
        r.Press(StellarKeyCode.U);
        var calls = r.Picker.Calls;
        Click(r);
        Assert.Equal(calls, r.Picker.Calls);
        Assert.Equal(r.Snapshot.LocalEntityId, r.Session.Subject);
        r.Shield.OverGameUi = false;   // open world again: clicks pick
        Click(r);
        Assert.Equal(new EntityId(7), r.Session.Subject);
    }

    [Fact]
    public void A_right_drag_that_starts_over_the_shown_game_UI_does_not_turn_the_camera()
    {
        var r = new SessionRig();
        r.Session.Enter();
        r.Press(StellarKeyCode.U);
        for (var i = 0; i < 10; i++) r.Frame();
        var before = (r.Camera.Control.LastYaw, r.Camera.Control.LastPitch);
        r.Shield.OverGameUi = true;
        r.Shield.Handle.Rmb = true;
        r.Shield.Handle.Delta = (40f, 0f);
        for (var i = 0; i < 10; i++) r.Frame();
        Assert.Equal(before, (r.Camera.Control.LastYaw, r.Camera.Control.LastPitch));
    }

    private static void Click(SessionRig r)
    {
        r.Shield.Handle.Lmb = true; r.Frame();
        r.Shield.Handle.Lmb = false; r.Frame();
    }
}
