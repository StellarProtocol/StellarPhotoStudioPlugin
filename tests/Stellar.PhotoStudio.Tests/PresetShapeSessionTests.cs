using System.Collections.Generic;
using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Presets;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

// Review 2026-10-03 (photo shapes, minor 1): the shape counts toward the preset's "modified" mark, a stashed
// "Unsaved look" carries it, Reset puts it back, and re-saving a preset saved before shapes keeps it shapeless unless
// the player changed the shape. Wired the way the plugin wires it: StudioSettings.ShapeChanged → OnShapeChanged.
public sealed class PresetShapeSessionTests
{
    private sealed class MemSection : IConfigSection
    {
        private readonly Dictionary<string, object?> _values = new();
        public T? Get<T>(string key, T? defaultValue) => _values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        public void Set<T>(string key, T value) => _values[key] = value;
        public void Save() { }
        public void SaveQuiet() { }
        public void RemoveByPrefix(string prefix) { }
    }

    private sealed class MemFiles : IPresetFiles
    {
        public readonly Dictionary<string, string> Files = new();
        public IEnumerable<string> List() => Files.Keys.ToList();
        public string? Read(string n) => Files.TryGetValue(n, out var j) ? j : null;
        public void Write(string n, string j) => Files[n] = j;
        public void Delete(string n) => Files.Remove(n);
    }

    private sealed record Rig(PresetSession Session, StudioSettings Settings, LookEditor Editor, PresetStore Store)
    {
        public Preset Get(string name) => Store.All.Single(p => p.Name == name);
    }

    private static Rig Make(MemFiles? files = null, string active = "Natural")
    {
        var settings = new StudioSettings(new MemSection());
        var store = new PresetStore(files ?? new MemFiles(), _ => { });
        var editor = new LookEditor();
        var session = new PresetSession(store, editor, active, null, new PresetShapeLink(() => settings.Shape, settings.SetShape));
        settings.ShapeChanged += session.OnShapeChanged;
        return new Rig(session, settings, editor, store);
    }

    private static MemFiles WithOldPreset()
    {
        var files = new MemFiles();
        files.Files["Old"] = "{\"Version\":1,\"Name\":\"Old\"}";   // saved before shapes existed
        return files;
    }

    [Fact]
    public void Changing_the_shape_marks_the_preset_modified()
    {
        var r = Make();
        Assert.False(r.Session.Modified);
        r.Settings.SetShape(PhotoShape.Portrait9x16);
        Assert.True(r.Session.Modified);
    }

    [Fact]
    public void Setting_the_same_shape_or_applying_a_preset_with_a_shape_does_not()
    {
        var r = Make();
        r.Settings.SetShape(PhotoShape.Screen);
        Assert.False(r.Session.Modified);
        r.Store.Save("Tall", new LookSettings(), PhotoShape.Portrait4x5);
        r.Session.Apply(r.Get("Tall"));
        Assert.Equal(PhotoShape.Portrait4x5, r.Settings.Shape);
        Assert.False(r.Session.Modified);
    }

    [Fact]
    public void A_stashed_unsaved_look_carries_its_shape_back()
    {
        var r = Make();
        r.Store.Save("Tall", new LookSettings(), PhotoShape.Portrait4x5);
        r.Settings.SetShape(PhotoShape.Square);            // a shape-only change is an unsaved look too
        r.Session.Apply(r.Get("Tall"));
        Assert.Equal(PhotoShape.Square, r.Session.Stash[0].Shape);
        Assert.Equal(PhotoShape.Portrait4x5, r.Settings.Shape);

        r.Session.RestoreUnsaved();
        Assert.Equal("Natural", r.Session.ActiveName);
        Assert.Equal(PhotoShape.Square, r.Settings.Shape);
        Assert.True(r.Session.Modified);
    }

    [Fact]
    public void Re_saving_an_old_preset_keeps_it_shapeless_while_the_shape_is_untouched()
    {
        var files = WithOldPreset();
        var r = Make(files, "Old");
        r.Settings.SetShape(PhotoShape.Portrait9x16);
        r.Session.ResetToSaved();                           // shape back to where it was; nothing marked
        r.Editor.EditColor(c => c with { Contrast = 3 });
        r.Session.Save();
        Assert.Null(new PresetStore(files, _ => { }).All.Single(p => p.Name == "Old").Shape);
    }

    [Fact]
    public void Re_saving_an_old_preset_stores_the_shape_the_player_changed()
    {
        var files = WithOldPreset();
        var r = Make(files, "Old");
        r.Settings.SetShape(PhotoShape.Portrait9x16);
        r.Session.Save();
        Assert.Equal(PhotoShape.Portrait9x16, new PresetStore(files, _ => { }).All.Single(p => p.Name == "Old").Shape);
    }

    [Fact]
    public void An_old_preset_applied_over_a_shape_and_re_saved_untouched_stays_shapeless()
    {
        var files = WithOldPreset();
        var r = Make(files);
        r.Settings.SetShape(PhotoShape.Wide21x9);
        r.Session.ResetToSaved();
        r.Settings.SetShape(PhotoShape.Wide21x9);           // the player's own shape, before choosing the preset
        r.Session.Apply(r.Get("Old"));                       // stashes the change; the shape stays (Old sets none)
        r.Session.Save();
        Assert.Equal(PhotoShape.Wide21x9, r.Settings.Shape);
        Assert.Null(new PresetStore(files, _ => { }).All.Single(p => p.Name == "Old").Shape);
    }

    [Fact]
    public void Reset_puts_back_the_shape_of_a_shapeless_preset()
    {
        var r = Make();
        r.Settings.SetShape(PhotoShape.Square);
        r.Session.ResetToSaved();
        Assert.Equal(PhotoShape.Screen, r.Settings.Shape);
        Assert.False(r.Session.Modified);
    }

    [Fact]
    public void Restored_shape_change_is_still_the_players_when_saved()
    {
        var files = WithOldPreset();
        var r = Make(files, "Old");
        r.Settings.SetShape(PhotoShape.Portrait2x3);
        r.Session.Apply(r.Get("Natural"));
        r.Session.RestoreUnsaved();
        Assert.Equal("Old", r.Session.ActiveName);
        r.Session.Save();
        Assert.Equal(PhotoShape.Portrait2x3, new PresetStore(files, _ => { }).All.Single(p => p.Name == "Old").Shape);
    }
}
