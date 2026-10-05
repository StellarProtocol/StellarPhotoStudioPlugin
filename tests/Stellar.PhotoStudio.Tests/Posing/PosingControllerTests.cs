using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Posing;
using Xunit;

namespace Stellar.PhotoStudio.Tests.Posing;

// Spec 2026-10-02 §§ 3–4 (approved mockup, Person group): one selection = the orbit subject; ‹ › cycles the people near
// you; selecting makes nothing (the framework makes the copy on the first control); Pose ▶/❚❚ + Moment, Expression
// ‹ › + Hold, Head/Eyes Default·Lens·Free + Lock + the arrow pad, Rotate ±180°, Reset this person; the copy note and the
// cloth hint; the panel forgets everything when the free camera ends.
public sealed class PosingControllerTests
{
    [Fact]
    public void Syncing_names_the_subject_from_one_people_read_and_makes_nothing()
    {
        var r = new PosingRig();
        Assert.Equal("Revette", r.Ctl.Person!.Name);
        Assert.Equal(PersonKind.Self, r.Ctl.Person.Kind);
        Assert.Equal(1, r.Posing.PeopleReads);
        Assert.Equal(0, r.Posing.Selects);
        r.Ctl.SyncSubject();
        Assert.Equal(1, r.Posing.PeopleReads);   // same subject: nothing read again
    }

    [Fact]
    public void Cycle_steps_through_the_people_and_wraps()
    {
        var r = new PosingRig();
        r.Ctl.Cycle(1);
        Assert.Equal(new EntityId(2), r.Subject);
        Assert.True(r.Ctl.IsCopy);
        r.Ctl.Cycle(1);
        Assert.Equal(new EntityId(3), r.Subject);
        r.Ctl.Cycle(1);
        Assert.Equal(new EntityId(1), r.Subject);
        r.Ctl.Cycle(-1);
        Assert.Equal(new EntityId(3), r.Subject);
    }

    [Fact]
    public void Cycle_from_someone_not_listed_starts_at_either_end()
    {
        var r = new PosingRig();
        r.Select(99);
        r.Ctl.Cycle(1);
        Assert.Equal(new EntityId(1), r.Subject);
        r.Select(99);
        r.Ctl.Cycle(-1);
        Assert.Equal(new EntityId(3), r.Subject);
    }

    [Fact]
    public void Play_then_pause_holds_at_the_live_moment_and_play_resumes()
    {
        var r = new PosingRig();
        r.Ctl.Play(PosingRig.Dance);
        r.Ctl.TogglePlay();
        Assert.False(r.Ctl.State.Playing);
        Assert.Equal(0.37f, r.Ctl.State.Moment);
        r.Ctl.TogglePlay();
        Assert.True(r.Ctl.State.Playing);
        Assert.Equal(new[] { "play 9020", "moment 0.37", "moment -1.00" }, r.Target(1).Calls);
    }

    [Fact]
    public void The_moment_slider_pauses_there_and_clamps()
    {
        var r = new PosingRig();
        r.Ctl.Play(PosingRig.Dance);
        r.Ctl.SetMoment(0.6f);
        Assert.False(r.Ctl.State.Playing);
        r.Ctl.SetMoment(2f);
        Assert.Equal(1f, r.Ctl.State.Moment);
        Assert.Equal(new[] { "play 9020", "moment 0.60", "moment 1.00" }, r.Target(1).Calls);
    }

    [Fact]
    public void Restart_replays_the_pose()
    {
        var r = new PosingRig();
        r.Ctl.Play(PosingRig.Dance);
        r.Ctl.SetMoment(0.5f);
        r.Ctl.Restart();
        Assert.True(r.Ctl.State.Playing);
        Assert.Equal(new[] { "play 9020", "moment 0.50", "play 9020" }, r.Target(1).Calls);
    }

    [Fact]
    public void Pause_and_moment_do_nothing_before_a_pose()
    {
        var r = new PosingRig();
        r.Ctl.TogglePlay();
        r.Ctl.SetMoment(0.4f);
        r.Ctl.Restart();
        Assert.Empty(r.Posing.Targets);
    }

    [Fact]
    public void Expressions_cycle_through_none_and_wrap()
    {
        var r = new PosingRig();
        r.Ctl.CycleExpression(1);
        Assert.Equal("Angry", r.Ctl.ExpressionName);
        r.Ctl.CycleExpression(1);
        r.Ctl.CycleExpression(1);
        Assert.Equal("", r.Ctl.ExpressionName);
        r.Ctl.CycleExpression(-1);
        Assert.Equal(new[] { "face 1003 hold=True", "face 1015 hold=True", "face 0 hold=True", "face 1015 hold=True" }, r.Target(1).Calls);
    }

    [Fact]
    public void Hold_is_on_by_default_and_toggling_reapplies_the_face()
    {
        var r = new PosingRig();
        Assert.True(r.Ctl.State.Hold);
        r.Ctl.CycleExpression(1);
        r.Ctl.ToggleHold();
        Assert.False(r.Ctl.State.Hold);
        Assert.Equal(new[] { "face 1003 hold=True", "face 1003 hold=False" }, r.Target(1).Calls);
    }

    [Fact]
    public void Look_modes_and_locks_go_to_the_target()
    {
        var r = new PosingRig();
        r.Ctl.SetLook(LookPart.Head, LookMode.Lens);
        r.Ctl.ToggleLock(LookPart.Head);
        r.Ctl.SetLook(LookPart.Eyes, LookMode.Free);
        Assert.Equal(LookMode.Lens, r.Ctl.State.Mode(LookPart.Head));
        Assert.True(r.Ctl.State.Locked(LookPart.Head));
        Assert.Equal(new[] { "look Head Lens lock=False", "look Head Lens lock=True", "look Eyes Free lock=False" }, r.Target(1).Calls);
    }

    [Fact]
    public void The_arrow_pad_steps_clamps_and_centres()
    {
        var r = new PosingRig();
        for (var i = 0; i < 5; i++) r.Ctl.Aim(LookPart.Head, 1, 0, PosingController.AimSteps[2]);   // Coarse = the old 0.25
        r.Ctl.Aim(LookPart.Head, 0, 1, PosingController.AimSteps[2]);
        r.Ctl.Aim(LookPart.Head, 0, 0, PosingController.AimSteps[2]);
        Assert.Equal(new[] { "aim Head 0.25,0.00", "aim Head 0.50,0.00", "aim Head 0.75,0.00", "aim Head 1.00,0.00", "aim Head 1.00,0.00",
            "aim Head 1.00,0.25", "aim Head 0.00,0.00" }, r.Target(1).Calls);
    }

    // Player request 2026-10-05: finer aim. Fine = 0.05 per press; the drag pad sets any point, clamped to ±1.
    [Fact]
    public void Fine_steps_move_a_twentieth_and_set_aim_clamps()
    {
        var r = new PosingRig();
        r.Ctl.Aim(LookPart.Eyes, -1, 0, PosingController.AimSteps[0]);
        r.Ctl.SetAim(LookPart.Eyes, 0.3f, 1.7f);
        Assert.Equal(new[] { "aim Eyes -0.05,0.00", "aim Eyes 0.30,1.00" }, r.Target(1).Calls);
        Assert.Equal(0.3f, r.Ctl.State.AimX(LookPart.Eyes), 3);
    }

    [Fact]
    public void Rotate_clamps_to_180()
    {
        var r = new PosingRig();
        r.Ctl.SetYaw(200f);
        Assert.Equal(180f, r.Ctl.State.Yaw);
        r.Ctl.SetYaw(-30f);
        Assert.Equal(new[] { "yaw 180", "yaw -30" }, r.Target(1).Calls);
    }

    [Fact]
    public void Reset_this_person_forgets_only_that_person()
    {
        var r = new PosingRig();
        r.Ctl.Play(PosingRig.Dance);
        r.Select(2);
        r.Ctl.Play(PosingRig.Dance);
        r.Ctl.ResetPerson();
        Assert.Equal("reset", r.Target(2).Calls[^1]);
        Assert.Null(r.Ctl.State.Action);
        r.Select(1);
        Assert.NotNull(r.Ctl.State.Action);
    }

    [Fact]
    public void The_copy_note_and_the_cloth_hint_follow_the_person_and_the_pause()
    {
        var r = new PosingRig();
        r.Select(2);
        Assert.True(r.Ctl.IsCopy);
        Assert.False(r.Ctl.ShowClothHint);
        r.Ctl.Play(PosingRig.Dance);
        Assert.False(r.Ctl.ShowClothHint);
        r.Ctl.TogglePlay();
        Assert.True(r.Ctl.ShowClothHint);
        r.Select(3);
        Assert.False(r.Ctl.IsCopy);
    }

    [Fact]
    public void Refused_and_unavailable_results_are_reported()
    {
        var r = new PosingRig();
        r.Posing.Targets[1] = new FakePoseTarget { PlayResult = PoseResult.Refused };
        r.Ctl.Play(PosingRig.Dance);
        Assert.Equal(PoseResult.Refused, r.Ctl.LastResult);
        r.Posing.Targets[1].PlayResult = PoseResult.Applied;
        r.Ctl.Play(PosingRig.Dance);
        Assert.Equal(new[] { PoseResult.Refused }, r.Refusals);
    }

    [Fact]
    public void Released_people_are_dropped_and_selected_again()
    {
        var r = new PosingRig();
        r.Ctl.Play(PosingRig.Dance);
        r.Posing.ResetAll();
        r.Ctl.OnPosingChanged();
        Assert.Null(r.Ctl.State.Action);
        r.Ctl.Play(PosingRig.Dance);
        Assert.Equal(2, r.Posing.Selects);
    }

    [Fact]
    public void Clear_forgets_everything()
    {
        var r = new PosingRig();
        r.Ctl.Play(PosingRig.Dance);
        r.Ctl.Clear();
        Assert.True(r.Ctl.Subject.IsNone);
        Assert.Null(r.Ctl.Person);
        Assert.Null(r.Ctl.State.Action);
    }

    [Fact]
    public void Nothing_happens_without_the_free_camera()
    {
        var r = new PosingRig();
        r.Posing.IsAvailable = false;
        r.Ctl.Play(PosingRig.Dance);
        r.Ctl.SetYaw(10f);
        Assert.False(r.Ctl.Available);
        Assert.Empty(r.Posing.Targets);
        Assert.Null(r.Ctl.State.Action);
    }

    [Fact]
    public void A_full_photo_list_shows_full_and_does_not_toast()
    {
        var r = new PosingRig();
        r.Select(2);
        r.Posing.Targets[2] = new FakePoseTarget { PlayResult = PoseResult.Full };
        r.Ctl.Play(PosingRig.Dance);
        Assert.True(r.Ctl.Full);
        Assert.Equal(PoseResult.Full, r.Ctl.LastResult);
        Assert.Empty(r.Refusals);   // the Person row says it; no toast
    }

    [Fact]
    public void Loading_and_failed_follow_the_target()
    {
        var r = new PosingRig();
        r.Select(3);
        r.Ctl.Play(PosingRig.Dance);
        r.Target(3).State = PoseTargetState.Loading;
        Assert.True(r.Ctl.Loading);
        r.Target(3).State = PoseTargetState.Failed;
        Assert.True(r.Ctl.Failed);
        Assert.False(r.Ctl.Loading);
    }

    // P1 (review 2026-10-02, plugin 1.2.0): a Failed or stuck-Loading person had no way out — the framework holds
    // Failed until Reset(). ResetPerson must recover both, forgetting the target so the next control starts fresh.
    [Fact]
    public void ResetPerson_recovers_a_failed_target_for_a_fresh_attempt()
    {
        var r = new PosingRig();
        r.Ctl.Play(PosingRig.Dance);
        r.Target(1).State = PoseTargetState.Failed;
        Assert.True(r.Ctl.Failed);
        var selectsBefore = r.Posing.Selects;
        r.Ctl.ResetPerson();
        Assert.False(r.Ctl.Failed);
        Assert.Equal("reset", r.Target(1).Calls[^1]);
        r.Ctl.Play(PosingRig.Dance);
        Assert.True(r.Posing.Selects > selectsBefore);   // forgotten: the next control re-selects, not reuses
        Assert.Equal(new[] { "play 9020", "reset", "play 9020" }, r.Target(1).Calls);
    }

    [Fact]
    public void ResetPerson_recovers_a_stuck_loading_target_for_a_fresh_attempt()
    {
        var r = new PosingRig();
        r.Ctl.Play(PosingRig.Dance);
        r.Target(1).State = PoseTargetState.Loading;
        Assert.True(r.Ctl.Loading);
        var selectsBefore = r.Posing.Selects;
        r.Ctl.ResetPerson();
        Assert.False(r.Ctl.Loading);
        Assert.Equal("reset", r.Target(1).Calls[^1]);
        r.Ctl.Play(PosingRig.Dance);
        Assert.True(r.Posing.Selects > selectsBefore);
        Assert.Equal(new[] { "play 9020", "reset", "play 9020" }, r.Target(1).Calls);
    }

    // P2 (review 2026-10-02): the camera can pick someone beyond PeopleRadius (40 m; it reaches ~60 m) — a picked
    // subject not in the known list must be looked up at the wider SubjectSearchRadius once, not left as "you".
    [Fact]
    public void A_subject_beyond_the_normal_radius_is_found_by_the_wider_search()
    {
        var r = new PosingRig();
        r.Select(4);   // "Dax", 60 m out — beyond PeopleRadius but inside SubjectSearchRadius
        Assert.NotNull(r.Ctl.Person);
        Assert.Equal("Dax", r.Ctl.Person!.Name);
        Assert.Equal(PersonKind.Player, r.Ctl.Person.Kind);
        Assert.False(r.Ctl.SubjectIsSelf);
    }

    [Fact]
    public void SubjectIsSelf_is_true_only_for_the_local_player_even_when_unresolved()
    {
        var r = new PosingRig();
        Assert.True(r.Ctl.SubjectIsSelf);   // starts on the local player (id 1)
        r.Select(999);                      // nobody known at any radius
        Assert.Null(r.Ctl.Person);
        Assert.False(r.Ctl.SubjectIsSelf);  // must not default to "you" just because Person is unresolved
        r.Select(1);
        Assert.True(r.Ctl.SubjectIsSelf);
    }

    [Fact]
    public void The_wider_search_runs_once_per_subject_not_every_sync()
    {
        var r = new PosingRig();
        var before = r.Posing.PeopleReads;
        r.Select(4);
        var afterFirstSync = r.Posing.PeopleReads;
        Assert.Equal(before + 2, afterFirstSync);   // the 40 m scan, then the 100 m fallback
        r.Ctl.SyncSubject();                        // same subject again: no further reads
        Assert.Equal(afterFirstSync, r.Posing.PeopleReads);
    }
}
