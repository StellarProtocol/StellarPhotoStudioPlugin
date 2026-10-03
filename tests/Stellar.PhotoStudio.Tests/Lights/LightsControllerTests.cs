using System.Linq;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Lights;
using Xunit;

namespace Stellar.PhotoStudio.Tests.Lights;

// Lights spec 2026-10-03 (owner-approved mockup v2): up to 8 lamps; drop at camera / move here; Around / Height /
// Distance relative to the selected person; duplicate; Light people; key + rim per person; scene lifetime.
public sealed class LightsControllerTests
{
    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }

    [Fact]
    public void A_lamp_drops_where_the_free_camera_is_and_is_selected()
    {
        var r = new LightsRig();
        Assert.Equal(LightsResult.Ok, r.Ctl.AddAtCamera());
        Near(new Vector3(1f, 1.6f, -3f), r.Lights.Pos(1));
        Assert.Equal(0, r.Ctl.SelectedIndex);
        Assert.True(r.Lights.Lamps[1].Enabled);
    }

    [Fact]
    public void Without_a_free_camera_nothing_drops()
    {
        var r = new LightsRig { Camera = null };
        Assert.Equal(LightsResult.NoCamera, r.Ctl.AddAtCamera());
        Assert.Empty(r.Lights.Calls);
    }

    [Fact]
    public void The_ninth_lamp_is_refused_before_asking_the_framework()
    {
        var r = new LightsRig();
        for (var i = 0; i < LightsController.MaxLamps; i++) Assert.Equal(LightsResult.Ok, r.Ctl.AddAtCamera());
        r.Lights.Calls.Clear();
        Assert.False(r.Ctl.CanAdd);
        Assert.Equal(LightsResult.Full, r.Ctl.AddAtCamera());
        Assert.Equal(LightsResult.Full, r.Ctl.Duplicate());
        Assert.Empty(r.Lights.Calls);
        Assert.Equal(8, r.Ctl.Count);
    }

    [Fact]
    public void A_refused_lamp_is_reported_unavailable_and_not_listed()
    {
        var r = new LightsRig();
        r.Lights.IsAvailable = false;
        Assert.Equal(LightsResult.Unavailable, r.Ctl.AddAtCamera());
        Assert.Equal(0, r.Ctl.Count);
    }

    [Fact]
    public void Move_here_moves_the_selected_lamp_to_the_camera()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Camera = (new Vector3(-2f, 3f, 4f), 90f);
        Assert.Equal(LightsResult.Ok, r.Ctl.MoveSelectedToCamera());
        Near(new Vector3(-2f, 3f, 4f), r.Lights.Pos(1));
    }

    [Fact]
    public void Placement_is_relative_to_the_selected_persons_place_and_facing()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Ctl.SetDistance(2f);
        r.Ctl.SetHeight(1.8f);
        r.Ctl.SetAround(0f);                                    // in front of you (you face +Z)
        Near(new Vector3(0f, 1.8f, 2f), r.Lights.Pos(1));
        r.Ctl.SetAround(90f);                                   // to your right (+X)
        Near(new Vector3(2f, 1.8f, 0f), r.Lights.Pos(1));
        r.Selected = new EntityId(2);                           // person 2 at (10,0,0) facing +X
        var p = r.Ctl.SelectedPlacement!.Value;                 // the same lamp, read around person 2
        Assert.Equal(180f, System.MathF.Abs(p.Around), 1);   // directly behind them
        Assert.Equal(8f, p.Distance, 3);
        r.Ctl.SetAround(0f);                                    // in front of person 2
        Near(new Vector3(18f, 1.8f, 0f), r.Lights.Pos(1));
    }

    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(-45f, 1.8f, 2f)]
    [InlineData(135f, -1f, 6f)]
    [InlineData(-170f, 3f, 0.5f)]
    public void Placement_round_trips_through_world(float around, float height, float distance)
    {
        var anchor = new LightAnchor(new Vector3(5f, 2f, -7f), 37f);
        var back = LightsMath.FromWorld(anchor, LightsMath.ToWorld(anchor, new LampPlacement(around, height, distance)));
        Assert.Equal(LightsMath.Wrap(around), back.Around, 2);
        Assert.Equal(height, back.Height, 3);
        Assert.Equal(distance, back.Distance, 3);
    }

    [Fact]
    public void Placement_needs_a_readable_person()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Anchors.Clear();
        Assert.Null(r.Ctl.SelectedPlacement);
        Assert.Equal(LightsResult.NoLamp, r.Ctl.SetAround(10f));
    }

    [Fact]
    public void Duplicate_copies_the_lamp_45_degrees_further_around_and_selects_it()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Ctl.SetDistance(2f);
        r.Ctl.SetHeight(1f);
        r.Ctl.SetAround(0f);
        r.Ctl.SetColor(new RgbColor(0.2f, 0.4f, 1f));
        Assert.Equal(LightsResult.Ok, r.Ctl.Duplicate());
        Assert.Equal(1, r.Ctl.SelectedIndex);
        Assert.Equal(new RgbColor(0.2f, 0.4f, 1f), r.Lights.Lamps[2].Color);
        Assert.Equal(45f, r.Ctl.SelectedPlacement!.Value.Around, 2);
        Assert.Equal(1f, r.Ctl.SelectedPlacement!.Value.Height, 3);
    }

    [Fact]
    public void Removing_keeps_a_sensible_selection_and_roles_follow_order()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Ctl.AddAtCamera();
        r.Ctl.AddAtCamera();
        Assert.Equal(new[] { LampRole.Key, LampRole.Fill, LampRole.Back }, r.Ctl.Lamps.Select(l => l.Role));
        r.Ctl.Remove(0);
        Assert.Equal(1, r.Ctl.SelectedIndex);          // still the same (third) lamp
        Assert.Equal(new[] { 1, 2 }, r.Ctl.Lamps.Select(l => l.Number));
        r.Ctl.Remove(1);
        Assert.Equal(0, r.Ctl.SelectedIndex);
        r.Ctl.Remove(0);
        Assert.Equal(-1, r.Ctl.SelectedIndex);
        Assert.Empty(r.Lights.Lamps);
    }

    [Fact]
    public void Light_people_is_forwarded_clamped_and_defaults_to_two()
    {
        var r = new LightsRig();
        Assert.Equal(LightsController.DefaultPeopleLevel, r.Lights.PeopleLevel);
        r.Ctl.SetPeopleLevel(99f);
        Assert.Equal(20f, r.Lights.PeopleLevel);
        r.Ctl.SetPeopleLevel(-1f);
        Assert.Equal(0f, r.Ctl.PeopleLevel);
    }

    [Fact]
    public void Lamp_on_off_strength_and_range_reach_the_framework()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Ctl.SetOn(0, false);
        r.Ctl.SetStrength(500f);
        r.Ctl.SetRange(3f);
        var s = r.Lights.Lamps[1];
        Assert.False(s.Enabled);
        Assert.Equal(LightLimits.MaxStrength, s.Strength);
        Assert.Equal(3f, s.Range);
    }

    [Fact]
    public void Touching_a_key_slider_turns_the_key_on_for_the_selected_person()
    {
        var r = new LightsRig();
        Assert.Equal(LightsResult.Ok, r.Ctl.SetKeyDirection(-30f));
        Assert.Equal(new KeyLight(-30f, 25f), r.Lights.People[new EntityId(1)].Key);
        Assert.Null(r.Lights.People[new EntityId(1)].Rim);
        r.Ctl.SetRimStrength(0.8f);
        Assert.Equal(0.8f, r.Lights.People[new EntityId(1)].Rim!.Value.Strength);
        Assert.Equal(1, r.Ctl.LitCount);
        r.Selected = new EntityId(2);
        Assert.False(r.Ctl.SelectedPersonLight.KeyOn);                  // per person
        r.Selected = new EntityId(1);
        Assert.Equal(LightsResult.Ok, r.Ctl.ResetPersonLight());
        Assert.False(r.Lights.People.ContainsKey(new EntityId(1)));
        Assert.Equal(0, r.Ctl.LitCount);
    }

    [Fact]
    public void Clear_removes_every_lamp_and_restores_every_person()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Ctl.AddAtCamera();
        r.Ctl.SetKeyOn(true);
        r.Ctl.Clear();
        Assert.Empty(r.Lights.Lamps);
        Assert.Empty(r.Lights.People);
        Assert.Equal(0, r.Ctl.SceneCount);
        Assert.Equal(-1, r.Ctl.SelectedIndex);
    }

    [Fact]
    public void A_framework_scene_end_forgets_the_lights_without_calling_back()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Ctl.SetKeyOn(true);
        r.Lights.Calls.Clear();
        var before = r.Changes;
        r.Lights.EndScene();
        Assert.Equal(0, r.Ctl.SceneCount);
        Assert.Empty(r.Lights.Calls);                                   // nothing to remove: the framework did it
        Assert.True(r.Changes > before);
        Assert.Equal(LightsController.DefaultPeopleLevel, r.Ctl.PeopleLevel);   // a setting, kept
    }

    [Fact]
    public void Dispose_unsubscribes_and_clears()
    {
        var r = new LightsRig();
        r.Ctl.AddAtCamera();
        r.Ctl.Dispose();
        Assert.Empty(r.Lights.Lamps);
        var changes = r.Changes;
        r.Lights.EndScene();
        Assert.Equal(changes, r.Changes);
    }
}
