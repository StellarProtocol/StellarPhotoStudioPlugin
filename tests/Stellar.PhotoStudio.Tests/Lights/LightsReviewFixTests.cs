using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;
using Stellar.PhotoStudio.Lights;
using Stellar.PhotoStudio.Presets;
using Stellar.PhotoStudio.Tests.FreeCam;
using Stellar.PhotoStudio.Tests.Posing;
using Xunit;

namespace Stellar.PhotoStudio.Tests.Lights;

// Lights review 2026-10-03 (qa of plugin 5bf3107/31b0160) + the owner's rulings that day:
// I-3 the lights go BEFORE the posing reset (a lit posed copy is written back while it exists);
// I-6 "Come back": lamps alone keep only the lights — the SCENE pill and Reset count them, but the entry hides come back and
//     re-entry starts from the game camera unless something is frozen / posed;
// I-5 Save keeps the stored lights when the scene has none, lights that differ from the active preset enable Save (tolerant
//     compare), and "Replace, with undo": applying a preset with lamps over hand-placed ones stashes them in the Unsaved row;
// minors: L / Shift+L edges (text-field suppression), preset clamps, Light people persisted, LampAt, pruning people who
//     left, the posed copy's Face turn, the lights' Unavailable result surfaced.
public sealed class LightsReviewFixTests
{
    private sealed class Rig
    {
        public readonly SceneRig Scene = new();
        public readonly FakeLights Lights = new();
        public readonly LightsController Ctl;

        public Rig()
        {
            Ctl = new LightsController(new LightsPorts(Lights, () => new EntityId(1),
                _ => new LightAnchor(Vector3.Zero, 0f), () => Scene.Cam.Session.ShownPose));
            Scene.Cam.Scene.TrackLights(() => Ctl.SceneCount, Ctl.Clear);
            Ctl.Changed += Scene.Cam.Scene.NotifyLightsChanged;
        }

        public SessionRig Cam => Scene.Cam;
    }

    // ── I-3 ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void I3_reset_scene_removes_the_lights_before_the_posing_reset()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Scene.Ctl.Select(new EntityId(2));
        r.Scene.Ctl.Play(PosingRig.Dance);
        r.Ctl.AddAtCamera();
        r.Ctl.SetKeyOn(true);
        var litAtPosingReset = -1;
        r.Scene.Posing.OnResetAll = () => litAtPosingReset = r.Lights.Lamps.Count + r.Lights.People.Count;
        r.Scene.ResetScene();
        Assert.Equal(0, litAtPosingReset);
    }

    [Fact]
    public void I3_unload_removes_the_lights_before_the_posing_reset()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Ctl.AddAtCamera();
        r.Ctl.SetRimOn(true);
        var litAtPosingReset = -1;
        r.Scene.Posing.OnResetAll = () => litAtPosingReset = r.Lights.Lamps.Count + r.Lights.People.Count;
        r.Cam.Session.Dispose();
        r.Cam.Scene.Dispose();
        Assert.Equal(0, litAtPosingReset);
    }

    // ── I-6 (owner ruling "Come back") ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void I6_lamps_alone_keep_the_lights_but_not_the_hides_or_the_camera_pose()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Ctl.AddAtCamera();
        r.Cam.Session.Exit();
        Assert.True(r.Cam.Scene.IsSet);                                  // the SCENE pill + Reset scene count the lamp
        Assert.Equal(1, r.Ctl.Count);
        Assert.False(r.Cam.Scene.KeepsCamera);
        Assert.False(r.Cam.Scene.HoldsEntryHide);                        // the HUD comes back
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
        Assert.False(r.Cam.Session.HasLastPose);                         // re-entry starts from the game camera
    }

    [Fact]
    public void I6_the_scene_refuses_to_keep_a_hide_for_lamps_alone()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Ctl.AddAtCamera();
        var hide = new FakeHandle();
        r.Cam.Scene.KeepEntryHide(hide, VisibilityLayers.GameHud);       // the scene's own guard, not just the session's
        Assert.Equal(1, hide.Disposed);
        Assert.False(r.Cam.Scene.HoldsEntryHide);
    }

    [Fact]
    public void I6_frozen_with_lamps_keeps_the_hides_and_the_pose()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Cam.Session.ToggleFreeze();
        r.Ctl.AddAtCamera();
        r.Cam.Session.Exit();
        Assert.True(r.Cam.Scene.HoldsEntryHide);
        Assert.True(r.Cam.Session.HasLastPose);
    }

    [Fact]
    public void I6_the_freeze_ending_while_lamps_remain_releases_the_hides_and_the_pose()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Cam.Session.ToggleFreeze();
        r.Ctl.AddAtCamera();
        r.Cam.Session.Exit();
        r.Cam.Scene.ToggleFreeze(null);                                  // unfreeze off the camera
        Assert.True(r.Cam.Scene.IsSet);                                  // the lamp is still there
        Assert.False(r.Cam.Scene.HoldsEntryHide);
        Assert.Equal(1, r.Cam.Visibility.Hides[0].Handle.Disposed);
        Assert.False(r.Cam.Session.HasLastPose);
    }

    [Fact]
    public void I6_reset_scene_with_only_lamps_clears_them_and_the_pill()
    {
        var r = new Rig();
        r.Cam.Session.Enter();
        r.Ctl.AddAtCamera();
        r.Cam.Session.Exit();
        r.Scene.ResetScene();
        Assert.Empty(r.Lights.Lamps);
        Assert.False(r.Cam.Scene.IsSet);
    }

    // ── I-5 presets ──────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class MemFiles : IPresetFiles
    {
        public readonly Dictionary<string, string> Files = new();
        public IEnumerable<string> List() => Files.Keys.ToList();
        public string? Read(string n) => Files.TryGetValue(n, out var j) ? j : null;
        public void Write(string n, string j) => Files[n] = j;
        public void Delete(string n) => Files.Remove(n);
    }

    private static (LightsRig R, PresetStore Store, PresetSession Session) Presets()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Ctl.SetAround(-45f);
        r.Ctl.SetHeight(1.8f);
        r.Ctl.SetDistance(2f);
        r.Ctl.AddAtCamera();
        r.Ctl.SetAround(60f);
        r.Ctl.SetColor(new RgbColor(0.2f, 0.45f, 1f));
        var store = new PresetStore(new MemFiles(), _ => { });
        var session = new PresetSession(store, new LookEditor(), "Natural", null)
            { Lights = new PresetLightsLink(r.Ctl.Capture, r.Ctl.Apply) };
        session.SaveAs("Rig");
        return (r, store, session);
    }

    [Fact]
    public void I5_save_with_no_lamps_keeps_the_stored_lights()
    {
        var (r, store, session) = Presets();
        r.Ctl.Clear();
        Assert.Null(r.Ctl.Capture());
        session.Save();
        Assert.Equal(2, store.All.Single(p => p.Name == "Rig").Lights!.Lamps.Count);
    }

    [Fact]
    public void I5_lights_that_differ_from_the_active_preset_enable_save()
    {
        var (r, _, session) = Presets();
        Assert.False(session.CanSave);                                   // just saved: nothing to store
        r.Ctl.SetAround(90f);                                            // the player moves a lamp
        Assert.False(session.Modified);                                  // lights never mark the LOOK modified …
        Assert.True(session.CanSave);                                    // … but Save has something to store
        session.Save();
        Assert.False(session.CanSave);
    }

    [Fact]
    public void I5_reapplying_a_saved_preset_reads_as_unchanged_despite_float_noise()
    {
        var (r, store, session) = Presets();
        var reloaded = store.All.Single(p => p.Name == "Rig");
        r.Ctl.Clear();
        session.Apply(reloaded);                                         // lamps re-placed through ToWorld / FromWorld
        Assert.False(session.CanSave);
        var stored = reloaded.Lights!;
        var nudged = stored with { Lamps = stored.Lamps.Select(l => l with { Placement = l.Placement with { Distance = l.Placement.Distance + 1e-5f } }).ToList() };
        Assert.True(stored.Equivalent(nudged));
        var moved = stored with { Lamps = stored.Lamps.Select(l => l with { Placement = l.Placement with { Distance = l.Placement.Distance + 0.05f } }).ToList() };
        Assert.False(stored.Equivalent(moved));
        Assert.True(stored.Equivalent(stored with { Key = null }) == (stored.Key is null));
    }

    [Fact]
    public void I5_applying_a_preset_over_hand_placed_lamps_stashes_them_and_one_click_restores_them()
    {
        var (r, store, session) = Presets();                             // "Rig": 2 lamps, active
        store.Save("One", new LookEditor().Build(), null,
            new LightsPreset(new[] { new PresetLamp(new LampPlacement(0f, 1f, 1f), new RgbColor(1, 1, 1), 10f, 4f, true) }, 2f, null, null));
        r.Ctl.AddAtCamera();                                             // hand-placed: 3 lamps now, differs from "Rig"
        var hand = r.Ctl.Capture()!;
        session.Apply(store.All.Single(p => p.Name == "One"));
        Assert.Equal(1, r.Ctl.Count);                                    // replaced
        Assert.Single(session.Stash);
        Assert.True(hand.Equivalent(session.Stash[0].Lights));
        Assert.False(session.Stash[0].LookEdited);
        session.RestoreUnsaved(0);
        Assert.Equal(3, r.Ctl.Count);                                    // back
        Assert.True(hand.Equivalent(r.Ctl.Capture()));
        Assert.False(session.Modified);                                  // the look itself was never changed
    }

    [Fact]
    public void I5_applying_over_lights_that_match_the_active_preset_stashes_nothing()
    {
        var (_, store, session) = Presets();
        store.Save("One", new LookEditor().Build(), null,
            new LightsPreset(new[] { new PresetLamp(new LampPlacement(0f, 1f, 1f), new RgbColor(1, 1, 1), 10f, 4f, true) }, 2f, null, null));
        session.Apply(store.All.Single(p => p.Name == "One"));
        Assert.Empty(session.Stash);
    }

    [Fact]
    public void The_lights_unavailable_result_is_returned_when_the_lamps_cannot_be_placed()
    {
        var (r, store, session) = Presets();
        r.Selected = new EntityId(99);                                   // nobody readable to place around
        Assert.Equal(LightsResult.Unavailable, session.Apply(store.All.Single(p => p.Name == "Rig")));
        Assert.Equal(LightsResult.Ok, session.Apply(store.All.Single(p => p.Name == "Natural")));   // no lights: Ok
    }

    // ── minors ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void L_drops_and_shift_L_moves_once_per_press_and_never_while_typing()
    {
        var input = new FreeCamInput();
        var h = new FakeShieldHandle();
        h.Held.Add(StellarKeyCode.L);
        var e = input.Read(h).Edges;
        Assert.True(e.DropLamp);
        Assert.False(e.MoveLamp);
        Assert.False(input.Read(h).Edges.DropLamp);                      // held: no repeat
        h.Held.Clear();
        input.Read(h);
        h.Held.Add(StellarKeyCode.L);
        h.Mods = ModifierKeys.Shift;
        e = input.Read(h).Edges;
        Assert.True(e.MoveLamp);
        Assert.False(e.DropLamp);
        h.Held.Clear();
        h.Mods = ModifierKeys.None;
        input.Read(h);
        h.Focused = true;                                                // a Stellar text field has the keyboard
        h.Held.Add(StellarKeyCode.L);
        e = input.Read(h).Edges;
        Assert.False(e.DropLamp || e.MoveLamp);
        h.Focused = false;                                               // focus leaves with L still held: never fires late
        e = input.Read(h).Edges;
        Assert.False(e.DropLamp || e.MoveLamp);
    }

    [Fact]
    public void Preset_files_are_clamped_to_what_the_controls_accept()
    {
        var dto = new LightsPresetDto
        {
            People = 999f,
            Lamps = new[] { new[] { 0f, 1f, 1f, 3f, -1f, float.NaN, 1e9f, 0f, 1f } },
            Key = new[] { 270f, 200f },
            Rim = new[] { 1f, 1f, 1f, 50f },
        };
        var p = dto.ToPreset();                                           // NaN can't be JSON — the in-memory DTO path
        Assert.Equal(LightLimits.MaxPeopleLevel, p.PeopleLevel);
        var lamp = p.Lamps.Single();
        Assert.Equal(new RgbColor(1f, 0f, 1f), lamp.Color);
        Assert.Equal(LightLimits.MaxStrength, lamp.Strength);
        Assert.Equal(LightLimits.MinRange, lamp.Range);
        Assert.Equal(-90f, p.Key!.Value.Direction);
        Assert.Equal(LightsMath.MaxKeyHeight, p.Key!.Value.Height);
        Assert.Equal(LightLimits.MaxRimStrength, p.Rim!.Value.Strength);
    }

    [Fact]
    public void Light_people_is_persisted_and_the_controller_starts_from_it()
    {
        var cfg = new MemConfigSection();
        var s = new StudioSettings(cfg);
        Assert.Equal(LightsController.DefaultPeopleLevel, s.PeopleLevel);
        s.SetPeopleLevel(7.5f, save: false);
        Assert.Equal(LightsController.DefaultPeopleLevel, new StudioSettings(cfg).PeopleLevel);   // not stored mid-drag
        s.SetPeopleLevel(7.5f, save: true);
        Assert.Equal(7.5f, new StudioSettings(cfg).PeopleLevel);
        s.SetPeopleLevel(99f, save: true);
        Assert.Equal(LightLimits.MaxPeopleLevel, new StudioSettings(cfg).PeopleLevel);
        var lights = new FakeLights();
        var ctl = new LightsController(new LightsPorts(lights, () => EntityId.None, _ => null, () => null), 7.5f);
        Assert.Equal(7.5f, ctl.PeopleLevel);
        Assert.Equal(7.5f, lights.PeopleLevel);
    }

    [Fact]
    public void LampAt_reads_one_row()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Ctl.AddAtCamera();
        r.Ctl.SetOn(1, false);
        Assert.Equal(r.Ctl.Lamps[1], r.Ctl.LampAt(1));
        Assert.False(r.Ctl.LampAt(1)!.On);
        Assert.Null(r.Ctl.LampAt(2));
        Assert.Null(r.Ctl.LampAt(-1));
    }

    [Fact]
    public void A_lit_person_the_game_no_longer_shows_is_dropped()
    {
        var r = new LightsRig();
        r.Selected = new EntityId(2);
        r.Ctl.SetKeyOn(true);
        Assert.Equal(1, r.Ctl.LitCount);
        Assert.False(r.Ctl.PrunePeople(_ => true));
        Assert.True(r.Ctl.PrunePeople(id => id.Value != 2));             // they left
        Assert.Equal(0, r.Ctl.LitCount);
        Assert.Equal(0, r.Ctl.SceneCount);
        Assert.False(r.Lights.People.ContainsKey(new EntityId(2)));      // the framework was told
    }

    [Fact]
    public void A_posed_persons_face_turn_is_reported_for_lamp_placement()
    {
        var rig = new SceneRig();
        rig.Cam.Session.Enter();
        rig.Ctl.Select(new EntityId(2));
        Assert.Equal(0f, rig.Ctl.PosedYaw(new EntityId(2)));
        rig.Ctl.SetYaw(40f);
        Assert.Equal(40f, rig.Ctl.PosedYaw(new EntityId(2)));
        Assert.Equal(0f, rig.Ctl.PosedYaw(new EntityId(3)));
    }

    [Fact]
    public void Placement_ranges_are_one_source()
    {
        var c = LightsMath.Clamp(new LampPlacement(500f, 99f, 99f));
        Assert.Equal(LightsMath.MaxHeight, c.Height);
        Assert.Equal(LightsMath.MaxDistance, c.Distance);
        Assert.InRange(c.Around, LightsMath.MinAround, LightsMath.MaxAround);
    }
}
