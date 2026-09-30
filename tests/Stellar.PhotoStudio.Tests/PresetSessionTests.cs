using System.Collections.Generic;
using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Presets;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

public sealed class PresetSessionTests
{
    private sealed class MemFiles : IPresetFiles
    {
        public readonly Dictionary<string, string> Files = new();
        public IEnumerable<string> List() => Files.Keys.ToList();
        public string? Read(string n) => Files.TryGetValue(n, out var j) ? j : null;
        public void Write(string n, string j) => Files[n] = j;
        public void Delete(string n) => Files.Remove(n);
    }

    private static (PresetSession s, LookEditor e, PresetStore store) Make(string active = "Natural")
    {
        var store = new PresetStore(new MemFiles(), _ => { });
        var e = new LookEditor();
        return (new PresetSession(store, e, active, null), e, store);
    }

    [Fact]
    public void Editing_marks_modified_but_applying_a_preset_does_not()
    {
        var (s, e, store) = Make();
        s.Apply(store.All.Single(p => p.Name == "Noir"));
        Assert.False(s.Modified);
        e.EditColor(c => c with { Contrast = 5 });
        Assert.True(s.Modified);
    }

    [Fact]
    public void Applying_over_edits_stashes_them_with_their_origin_and_restore_returns_to_it()
    {
        var (s, e, store) = Make();
        s.Apply(store.All.Single(p => p.Name == "Cinematic"));
        e.EditColor(c => c with { Saturation = -77 });
        s.Apply(store.All.Single(p => p.Name == "Film"));

        Assert.Equal("Cinematic", s.Stash[0].Origin);
        s.RestoreUnsaved();
        Assert.Equal("Cinematic", s.ActiveName);
        Assert.True(s.Modified);
        Assert.Equal(-77, e.Build().Color!.Saturation);
        Assert.Empty(s.Stash);
    }

    [Fact]
    public void Restoring_while_modified_swaps_the_current_edits_into_the_stash()
    {
        var (s, e, store) = Make();
        s.Apply(store.All.Single(p => p.Name == "Cinematic"));
        e.EditColor(c => c with { Saturation = -10 });
        s.Apply(store.All.Single(p => p.Name == "Film"));
        e.EditColor(c => c with { Saturation = -20 });

        s.RestoreUnsaved(0);

        Assert.Equal("Cinematic", s.ActiveName);
        Assert.Equal(-10, e.Build().Color!.Saturation);
        Assert.Equal("Film", s.Stash[0].Origin);
        Assert.Equal(-20, s.Stash[0].Look.Color!.Saturation);
    }

    [Fact]
    public void Several_set_aside_edits_are_all_kept_newest_first()
    {
        var (s, e, store) = Make();
        foreach (var name in new[] { "Cinematic", "Noir", "Film" })
        {
            s.Apply(store.All.Single(p => p.Name == name));
            e.EditColor(c => c with { Contrast = 1 });
        }
        s.Apply(store.All.Single(p => p.Name == "Natural"));
        Assert.Equal(new[] { "Film", "Noir", "Cinematic" }, s.Stash.Select(x => x.Origin));
    }

    [Fact]
    public void Rename_carries_stashed_edits_to_the_new_name()
    {
        var (s, e, store) = Make();
        s.SaveAs("Mine");
        e.EditColor(c => c with { Contrast = 9 });
        s.Apply(store.All.Single(p => p.Name == "Noir"));
        s.Apply(store.All.Single(p => p.Name == "Mine"));
        s.Rename("Mine 2");
        Assert.Equal("Mine 2", s.Stash[0].Origin);
    }

    [Fact]
    public void Renaming_a_modified_preset_keeps_the_editor_look_and_modified_flag()
    {
        var (s, e, _) = Make();
        s.SaveAs("Mine");
        e.EditBloom(b => b with { Intensity = 4 });
        s.Rename("Mine 2");

        Assert.Equal("Mine 2", s.ActiveName);
        Assert.True(s.Modified);
        Assert.Equal(4, e.Build().Bloom!.Intensity);
    }

    [Fact]
    public void Reset_to_saved_discards_edits_without_stashing()
    {
        var (s, e, _) = Make();
        e.EditColor(c => c with { Contrast = 50 });
        s.ResetToSaved();
        Assert.False(s.Modified);
        Assert.Empty(s.Stash);
        Assert.Null(e.Build().Color);   // Natural has no groups
    }

    [Fact]
    public void Save_as_then_delete_returns_to_the_first_preset()
    {
        var (s, _, store) = Make();
        s.SaveAs("Mine");
        Assert.False(s.ActiveIsBuiltIn);
        s.Delete();
        Assert.Equal(store.All[0].Name, s.ActiveName);
        Assert.DoesNotContain(store.All, p => p.Name == "Mine");
    }

    [Fact]
    public void Built_in_save_and_delete_are_no_ops()
    {
        var (s, e, store) = Make("Noir");
        e.EditColor(c => c with { Contrast = 1 });
        s.Save();
        s.Delete();
        Assert.Equal("Noir", s.ActiveName);
        Assert.True(s.Modified);
        Assert.Equal(5, store.All.Count);
    }

    [Fact]
    public void A_working_look_restores_as_modified()
    {
        var store = new PresetStore(new MemFiles(), _ => { });
        var e = new LookEditor();
        var s = new PresetSession(store, e, "Film", new LookSettings { Vignette = new VignetteLook() });
        Assert.Equal("Film", s.ActiveName);
        Assert.True(s.Modified);
        Assert.NotNull(e.Build().Vignette);
    }
}
