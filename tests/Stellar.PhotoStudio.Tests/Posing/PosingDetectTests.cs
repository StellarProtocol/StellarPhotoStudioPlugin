using Stellar.Abstractions.Domain;
using Xunit;

namespace Stellar.PhotoStudio.Tests.Posing;

// Owner bug 2026-10-02 (MAIN): selecting a person who is ALREADY doing an emote showed "Pick a pose" + Moment 0 %. The
// panel must show their running action, playing, with the Moment following it; ❚❚ holds it where it is without playing
// it again; the user's own pick wins from then on; Reset re-enables detection. Pinned regression tests — never weaken.
public sealed class PosingDetectTests
{
    [Fact]
    public void Selecting_someone_mid_emote_shows_it_playing_at_its_moment()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9020, 0.42f);
        r.Select(2);
        var s = r.Ctl.State;
        Assert.Equal(PosingRig.Dance, s.Action);
        Assert.True(s.Playing);
        Assert.True(s.Detected);
        Assert.Equal(0.42f, s.Moment);
        Assert.Equal(0, r.Posing.Selects);   // reading makes nothing: no copy, no target
    }

    [Fact]
    public void The_moment_follows_the_running_action_on_each_poll()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9020, 0.1f);
        r.Select(2);
        r.Posing.Running[2] = (9020, 0.6f);
        Assert.True(r.Ctl.PollCurrentAction());
        Assert.Equal(0.6f, r.Ctl.State.Moment);
        Assert.False(r.Ctl.PollCurrentAction());   // nothing changed: no redraw asked
    }

    [Fact]
    public void An_action_not_in_the_unlocked_list_gets_the_generic_label_with_its_id()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9206, 0.3f);
        r.Select(2);
        Assert.Equal(9206, r.Ctl.State.Action!.Id);
        Assert.Equal(PosingRig.CurrentPose, r.Ctl.State.Action.Name);
    }

    [Fact]
    public void Nothing_running_still_shows_pick_a_pose()
    {
        var r = new PosingRig();
        r.Select(2);
        Assert.Null(r.Ctl.State.Action);
        Assert.False(r.Ctl.State.Playing);
        Assert.Equal(0f, r.Ctl.State.Moment);
    }

    [Fact]
    public void When_the_detected_action_ends_the_panel_returns_to_pick_a_pose()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9020, 0.9f);
        r.Select(2);
        r.Posing.Running.Remove(2);
        Assert.True(r.Ctl.PollCurrentAction());
        Assert.Null(r.Ctl.State.Action);
        Assert.False(r.Ctl.State.Playing);
        Assert.False(r.Ctl.State.Detected);
    }

    [Fact]
    public void Pausing_a_detected_action_holds_it_at_the_current_moment_without_playing_it()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9020, 0.3f);
        r.Select(2);
        r.Posing.Running[2] = (9020, 0.55f);   // it kept playing since the last poll
        r.Ctl.TogglePlay();
        Assert.False(r.Ctl.State.Playing);
        Assert.Equal(0.55f, r.Ctl.State.Moment);
        Assert.Equal(new[] { "moment 0.55" }, r.Target(2).Calls);   // no "play": never restarted
    }

    [Fact]
    public void Scrubbing_a_detected_action_holds_it_there_without_playing_it()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9020, 0.3f);
        r.Select(2);
        r.Ctl.SetMoment(0.8f);
        Assert.Equal(new[] { "moment 0.80" }, r.Target(2).Calls);
        Assert.Equal(PosingRig.Dance, r.Ctl.State.Action);
    }

    [Fact]
    public void Restart_on_a_detected_action_plays_it_from_the_start()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9020, 0.3f);
        r.Select(2);
        r.Ctl.Restart();
        Assert.Equal(new[] { "play 9020" }, r.Target(2).Calls);
    }

    [Fact]
    public void After_a_pause_detection_stops_and_our_state_wins()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9020, 0.3f);
        r.Select(2);
        r.Ctl.TogglePlay();
        r.Posing.Running[2] = (9100, 0.9f);
        Assert.False(r.Ctl.PollCurrentAction());
        Assert.Equal(PosingRig.Dance, r.Ctl.State.Action);
        Assert.Equal(0.3f, r.Ctl.State.Moment);
        Assert.False(r.Ctl.State.Playing);
    }

    [Fact]
    public void A_user_pick_overrides_detection()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9206, 0.3f);
        r.Select(2);
        var wave = new EmoteInfo(9011, "Wave", "", false);
        r.Ctl.Play(wave);
        Assert.False(r.Ctl.PollCurrentAction());
        Assert.Equal(wave, r.Ctl.State.Action);
        Assert.Equal(0f, r.Ctl.State.Moment);
    }

    [Fact]
    public void Reset_re_enables_detection()
    {
        var r = new PosingRig();
        r.Posing.Running[2] = (9020, 0.3f);
        r.Select(2);
        r.Ctl.TogglePlay();
        r.Ctl.ResetPerson();
        r.Posing.Running[2] = (9206, 0.7f);
        Assert.True(r.Ctl.PollCurrentAction());
        Assert.Equal(9206, r.Ctl.State.Action!.Id);
        Assert.True(r.Ctl.State.Playing);
        Assert.Equal(0.7f, r.Ctl.State.Moment);
    }

    [Fact]
    public void Nothing_is_read_while_posing_is_unavailable()
    {
        var r = new PosingRig();
        r.Posing.IsAvailable = false;
        var before = r.Posing.ActionReads;
        Assert.False(r.Ctl.PollCurrentAction());
        Assert.Equal(before, r.Posing.ActionReads);
    }
}
