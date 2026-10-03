using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Lights;
using Stellar.PhotoStudio.Presets;
using Xunit;

namespace Stellar.PhotoStudio.Tests.Lights;

// Lights spec § 5: presets save lamps RELATIVE to the selected person (+ Light people + the selected person's key / rim);
// applying places them around whoever is selected now. Lights never mark a preset modified and Reset all leaves them.
public sealed class LightsPresetTests
{
    private sealed class MemFiles : IPresetFiles
    {
        public readonly Dictionary<string, string> Files = new();
        public IEnumerable<string> List() => Files.Keys.ToList();
        public string? Read(string n) => Files.TryGetValue(n, out var j) ? j : null;
        public void Write(string n, string j) => Files[n] = j;
        public void Delete(string n) => Files.Remove(n);
    }

    private static LightsRig ThreeLampRig()
    {
        var r = new LightsRig();
        float[][] rig = { new[] { -45f, 1.8f, 2f }, new[] { 60f, 1.2f, 3f }, new[] { 180f, 2.5f, 1.5f } };
        foreach (var a in rig)
        {
            r.Ctl.AddAtCamera();
            r.Ctl.SetAround(a[0]);
            r.Ctl.SetHeight(a[1]);
            r.Ctl.SetDistance(a[2]);
        }
        r.Ctl.SetColor(new RgbColor(0.2f, 0.45f, 1f));
        r.Ctl.SetOn(1, false);
        r.Ctl.SetPeopleLevel(3f);
        r.Ctl.SetKeyDirection(-60f);
        r.Ctl.SetRimStrength(0.45f);
        return r;
    }

    [Fact]
    public void Capture_is_relative_and_round_trips_through_the_json_dto()
    {
        var r = ThreeLampRig();
        var captured = r.Ctl.Capture()!;
        Assert.Equal(3, captured.Lamps.Count);
        Assert.Equal(-45f, captured.Lamps[0].Placement.Around, 2);
        Assert.False(captured.Lamps[1].On);
        var json = JsonSerializer.Serialize(LightsPresetDto.From(captured));
        var back = JsonSerializer.Deserialize<LightsPresetDto>(json)!.ToPreset();
        Assert.Equal(captured.PeopleLevel, back.PeopleLevel);
        Assert.Equal(captured.Key, back.Key);
        Assert.Equal(captured.Rim, back.Rim);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(captured.Lamps[i].Placement.Around, back.Lamps[i].Placement.Around, 3);
            Assert.Equal(captured.Lamps[i].Placement.Height, back.Lamps[i].Placement.Height, 3);
            Assert.Equal(captured.Lamps[i].Placement.Distance, back.Lamps[i].Placement.Distance, 3);
            Assert.Equal(captured.Lamps[i] with { Placement = default }, back.Lamps[i] with { Placement = default });
        }
    }

    [Fact]
    public void Capture_is_relative_to_the_selected_person_not_the_world()
    {
        var r = new LightsRig();
        r.Selected = new EntityId(2);                       // person 2 at (10,0,0) facing +X
        r.Ctl.AddAtCamera();
        r.Ctl.SetAround(30f);
        r.Ctl.SetHeight(1.5f);
        r.Ctl.SetDistance(2.5f);
        var lamp = r.Ctl.Capture()!.Lamps[0].Placement;
        Assert.Equal(30f, lamp.Around, 2);
        Assert.Equal(1.5f, lamp.Height, 3);
        Assert.Equal(2.5f, lamp.Distance, 3);
    }

    [Fact]
    public void Applying_places_the_lamps_around_whoever_is_selected_now()
    {
        var r = ThreeLampRig();
        var preset = r.Ctl.Capture()!;
        r.Ctl.Clear();
        r.Selected = new EntityId(2);                       // person 2 at (10,0,0) facing +X
        Assert.Equal(LightsResult.Ok, r.Ctl.Apply(preset));
        Assert.Equal(3, r.Ctl.Count);
        var p = r.Ctl.Lamps[0].Position;
        var expected = LightsMath.ToWorld(new LightAnchor(new Vector3(10f, 0f, 0f), 90f), preset.Lamps[0].Placement);
        Assert.Equal(expected.X, p.X, 3);
        Assert.Equal(expected.Z, p.Z, 3);
        Assert.Equal(3f, r.Lights.PeopleLevel);
        Assert.Equal(preset.Key, r.Lights.People[new EntityId(2)].Key);
        Assert.False(r.Lights.Lamps.Values.ElementAt(1).Enabled);
    }

    [Fact]
    public void Applying_replaces_the_scenes_lamps()
    {
        var r = ThreeLampRig();
        var preset = r.Ctl.Capture()! with { Lamps = new[] { new PresetLamp(new LampPlacement(0f, 1f, 1f), new RgbColor(1, 1, 1), 10f, 4f, true) } };
        r.Ctl.Apply(preset);
        Assert.Equal(1, r.Ctl.Count);
        Assert.Single(r.Lights.Lamps);
    }

    [Fact]
    public void Nothing_to_save_captures_null()
    {
        var r = new LightsRig();
        Assert.Null(r.Ctl.Capture());
    }

    [Fact]
    public void Presets_carry_the_lights_through_save_load_export_and_import()
    {
        var r = ThreeLampRig();
        var files = new MemFiles();
        var store = new PresetStore(files, _ => { });
        var editor = new LookEditor();
        var session = new PresetSession(store, editor, "Natural", null) { Lights = new PresetLightsLink(r.Ctl.Capture, p => r.Ctl.Apply(p)) };
        session.SaveAs("Rig");
        var reloaded = new PresetStore(files, _ => { }).All.Single(p => p.Name == "Rig");
        Assert.True(r.Ctl.Capture()!.Equivalent(reloaded.Lights) || Close(r.Ctl.Capture()!, reloaded.Lights!));
        var imported = store.Import(store.Export("Rig"))!;
        Assert.True(Close(r.Ctl.Capture()!, imported.Lights!));

        r.Ctl.Clear();
        session.Apply(store.All.Single(p => p.Name == "Natural"));   // a built-in carries no lights: the scene is left alone
        Assert.Equal(0, r.Ctl.Count);
        session.Apply(reloaded);
        Assert.Equal(3, r.Ctl.Count);
    }

    [Fact]
    public void Lights_never_mark_a_preset_modified_and_reset_all_leaves_them()
    {
        var r = ThreeLampRig();
        var store = new PresetStore(new MemFiles(), _ => { });
        var session = new PresetSession(store, new LookEditor(), "Natural", null) { Lights = new PresetLightsLink(r.Ctl.Capture, p => r.Ctl.Apply(p)) };
        session.SaveAs("Rig");
        r.Ctl.AddAtCamera();
        Assert.False(session.Modified);
        session.ResetToSaved();
        Assert.Equal(4, r.Ctl.Count);   // Reset all re-applies the LOOK only
    }

    private static bool Close(LightsPreset a, LightsPreset b) =>
        a.Lamps.Count == b.Lamps.Count && a.PeopleLevel == b.PeopleLevel && a.Key == b.Key && a.Rim == b.Rim &&
        a.Lamps.Zip(b.Lamps).All(t => System.MathF.Abs(t.First.Placement.Around - t.Second.Placement.Around) < 1e-3f &&
                                      System.MathF.Abs(t.First.Placement.Distance - t.Second.Placement.Distance) < 1e-3f &&
                                      t.First.Color == t.Second.Color && t.First.On == t.Second.On);
}
