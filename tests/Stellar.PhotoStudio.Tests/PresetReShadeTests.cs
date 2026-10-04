using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Presets;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

public sealed class PresetReShadeTests
{
    private sealed class MemFiles : IPresetFiles
    {
        public readonly Dictionary<string, string> Files = new();
        public IEnumerable<string> List() => Files.Keys.ToList();
        public string? Read(string n) => Files.TryGetValue(n, out var j) ? j : null;
        public void Write(string n, string j) => Files[n] = j;
        public void Delete(string n) => Files.Remove(n);
    }

    private readonly MemFiles _files = new();
    private ReShadeChoice? _live = new("Noir.ini", true);
    private readonly List<ReShadeChoice> _applied = new();

    private (PresetSession s, PresetStore store) Make()
    {
        var store = new PresetStore(_files, _ => { });
        var s = new PresetSession(store, new LookEditor(), "Natural", null)
        {
            ReShadeLink = new PresetReShadeLink(() => _live, c => { _applied.Add(c); _live = c; }),
        };
        return (s, store);
    }

    [Fact]
    public void Dto_round_trips_the_choice_as_a_file_name_only()
    {
        var dto = PresetDto.From("Mine", new LookSettings(), reshade: new ReShadeChoice(@"C:\g\x.data\reshade\presets\Golden hour.ini", false));
        var back = JsonSerializer.Deserialize<PresetDto>(JsonSerializer.Serialize(dto))!;
        Assert.Equal(new ReShadeChoice("Golden hour.ini", false), back.ToReShade());
    }

    // Rollback safety (process rules § 6): the key is additive — a 1.4.0 file has none and reads as "no choice".
    [Fact]
    public void A_preset_file_without_reshade_reads_as_no_choice()
    {
        var back = JsonSerializer.Deserialize<PresetDto>("{\"Version\":1,\"Name\":\"Old\"}")!;
        Assert.Null(back.ToReShade());
        Assert.Null(PresetDto.From("x", new LookSettings()).ReShade);
    }

    [Fact]
    public void Save_as_captures_the_current_choice_and_applying_it_later_restores_it()
    {
        var (s, store) = Make();
        s.SaveAs("Mine");
        Assert.Equal(new ReShadeChoice("Noir.ini", true), store.All.Single(p => p.Name == "Mine").ReShade);
        _live = new ReShadeChoice("Other.ini", false);
        s.Apply(store.All.Single(p => p.Name == "Natural"));     // built-in: no choice, ReShade untouched
        Assert.Empty(_applied);
        s.Apply(store.All.Single(p => p.Name == "Mine"));
        Assert.Equal(new ReShadeChoice("Noir.ini", true), _applied.Single());
    }

    [Fact]
    public void A_reshade_edit_marks_the_look_modified_and_save_keeps_it()
    {
        var (s, store) = Make();
        store.Save("Plain", new LookSettings());                  // a pre-1.5 preset: no choice stored
        s.Apply(store.All.Single(p => p.Name == "Plain"));
        s.Save();
        Assert.Null(store.All.Single(p => p.Name == "Plain").ReShade);   // untouched → still none
        _live = new ReShadeChoice("Golden hour.ini", true);
        s.OnReShadeEdited();
        Assert.True(s.Modified);
        s.Save();
        Assert.Equal(new ReShadeChoice("Golden hour.ini", true), store.All.Single(p => p.Name == "Plain").ReShade);
    }

    [Fact]
    public void Saving_on_a_client_without_reshade_keeps_the_stored_choice()
    {
        var (s, store) = Make();
        store.Save("Mine", new LookSettings(), reshade: new ReShadeChoice("Noir.ini", true));
        s.Apply(store.All.Single(p => p.Name == "Mine"));
        _live = null;                                              // NotInstalled
        s.Save();
        Assert.Equal(new ReShadeChoice("Noir.ini", true), store.All.Single(p => p.Name == "Mine").ReShade);
    }

    [Fact]
    public void The_unsaved_look_stash_carries_the_choice_back()
    {
        var (s, store) = Make();
        _live = new ReShadeChoice("Edited.ini", false);
        s.OnReShadeEdited();
        s.Apply(store.All.Single(p => p.Name == "Noir"));          // stashes the edited state
        Assert.Equal(new ReShadeChoice("Edited.ini", false), s.Stash[0].ReShade);
        _applied.Clear();
        s.RestoreUnsaved();
        Assert.Equal(new ReShadeChoice("Edited.ini", false), _applied.Single());
    }

    [Fact]
    public void Rename_export_and_import_keep_the_choice()
    {
        var (_, store) = Make();
        store.Save("A", new LookSettings(), reshade: new ReShadeChoice("Noir.ini", false));
        store.Rename("A", "B");
        Assert.Equal(new ReShadeChoice("Noir.ini", false), store.All.Single(p => p.Name == "B").ReShade);
        var imported = store.Import(store.Export("B"))!;
        Assert.Equal(new ReShadeChoice("Noir.ini", false), imported.ReShade);
    }

    // --- Orchestrator-required rollback-safety tests (process rules § 6) ---

    [Fact]
    public void A_1_4_0_format_preset_file_with_no_reshade_key_loads_unchanged()
    {
        // Hand-written: exactly what a real 1.4.0 build wrote to disk — no "ReShade" property at all.
        _files.Files["Old Style"] = "{\"Version\":1,\"Name\":\"Old Style\",\"Dof\":[1.5,2.8,50,1]," +
            "\"Color\":[0.1,15.0,-5.0,1.0,1.0,1.0],\"Shape\":\"screen\"}";

        var store = new PresetStore(_files, _ => Assert.Fail("a well-formed 1.4.0 file must not warn"));
        var loaded = store.All.Single(p => p.Name == "Old Style");

        Assert.Null(loaded.ReShade);
        Assert.NotNull(loaded.Look.Dof);
        Assert.Equal(1.5f, loaded.Look.Dof!.FocusDistance);
        Assert.Equal(PhotoShapes.TryParse("screen"), loaded.Shape);
    }

    [Fact]
    public void A_1_5_0_preset_round_trips_through_store_save_and_reload()
    {
        var store1 = new PresetStore(_files, _ => { });
        store1.Save("Round Trip", new LookSettings { Color = new ColorLook { Saturation = -10f } },
            shape: PhotoShapes.TryParse("screen"), reshade: new ReShadeChoice("Noir.ini", true));

        // Reload from the same backing files as a fresh process would on next launch.
        var store2 = new PresetStore(_files, _ => { });
        var reloaded = store2.All.Single(p => p.Name == "Round Trip");

        Assert.Equal(new ReShadeChoice("Noir.ini", true), reloaded.ReShade);
        Assert.Equal(-10f, reloaded.Look.Color!.Saturation);
        Assert.Equal(PhotoShapes.TryParse("screen"), reloaded.Shape);
    }

    /// <summary>A MINIMAL legacy-shaped stand-in for an older build's on-disk DTO — NOT the exact 1.4.0
    /// <see cref="PresetDto"/> (that also carried a <c>Lights</c> property); only enough fields to prove an
    /// older reader ignores the new <c>ReShade</c> key rather than throwing or losing other data.</summary>
    private sealed class LegacyPresetDto
    {
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public float[]? Dof { get; set; }
        public float[]? Color { get; set; }
        public float[]? WhiteBalance { get; set; }
        public string? LutFile { get; set; }
        public float LutContribution { get; set; } = 1f;
        public float[]? Bloom { get; set; }
        public float[]? Vignette { get; set; }
        public float[]? FilmGrain { get; set; }
        public string? Shape { get; set; }
    }

    // Fix round 1: ResetToSaved must restore the ORIGINAL baseline on every call, not the previous reset's
    // discarded edit — Apply(preset) stamps _baseReShade from the still-live (not-yet-reverted) value, so
    // without re-stamping it from the just-applied revert choice, a second Reset regresses to the first
    // discarded edit instead of the true baseline.
    [Fact]
    public void Reset_all_twice_reverts_to_the_original_baseline_not_the_first_discarded_edit()
    {
        var (s, store) = Make();
        var natural = store.All.Single(p => p.Name == "Natural");   // carries no ReShade choice

        _live = new ReShadeChoice("A.ini", true);
        s.Apply(natural);                                            // establishes the baseline: A

        _live = new ReShadeChoice("B.ini", true);
        s.OnReShadeEdited();
        _applied.Clear();
        s.ResetToSaved();
        Assert.Equal(new ReShadeChoice("A.ini", true), _applied.Single());   // first reset: back to A

        _live = new ReShadeChoice("C.ini", true);
        s.OnReShadeEdited();
        _applied.Clear();
        s.ResetToSaved();
        // Must revert to the ORIGINAL baseline A, not the first discarded edit B.
        Assert.Equal(new ReShadeChoice("A.ini", true), _applied.Single());
    }

    // Review fix (qa blocker): restoring an "Unsaved look" row that carries a ReShade edit must keep it an EDIT — Save
    // stores it, and applying another preset sets it aside again — not silently drop it.
    [Fact]
    public void Restore_then_save_keeps_the_reshade_edit()
    {
        var (s, store) = Make();
        store.Save("Plain", new LookSettings());
        s.Apply(store.All.Single(p => p.Name == "Plain"));
        _live = new ReShadeChoice("Golden.ini", true);
        s.OnReShadeEdited();
        s.Apply(store.All.Single(p => p.Name == "Natural"));     // stash
        s.RestoreUnsaved();                                        // back on Plain with the edit
        s.Save();
        Assert.Equal(new ReShadeChoice("Golden.ini", true), store.All.Single(p => p.Name == "Plain").ReShade);
    }

    [Fact]
    public void Restore_then_apply_other_restashes_the_reshade_edit()
    {
        var (s, store) = Make();
        store.Save("Plain", new LookSettings());
        s.Apply(store.All.Single(p => p.Name == "Plain"));
        _live = new ReShadeChoice("Golden.ini", true);
        s.OnReShadeEdited();
        s.Apply(store.All.Single(p => p.Name == "Natural"));
        s.RestoreUnsaved();
        s.Apply(store.All.Single(p => p.Name == "Natural"));     // set aside again
        Assert.Equal(new ReShadeChoice("Golden.ini", true), s.Stash[0].ReShade);
    }

    // The restored row's baseline comes back too: Reset all after a restore reverts to the ReShade choice the row's
    // preset started from, not to whatever the preset that was applied in between left behind.
    [Fact]
    public void Restore_then_reset_all_reverts_to_the_rows_own_baseline()
    {
        var (s, store) = Make();
        store.Save("Plain", new LookSettings());
        _live = new ReShadeChoice("Base.ini", true);
        s.Apply(store.All.Single(p => p.Name == "Plain"));       // baseline: Base
        _live = new ReShadeChoice("Golden.ini", true);
        s.OnReShadeEdited();
        store.Save("Other", new LookSettings(), reshade: new ReShadeChoice("Other.ini", false));
        s.Apply(store.All.Single(p => p.Name == "Other"));       // stash Plain+Golden; baseline now Other
        s.RestoreUnsaved();
        _applied.Clear();
        s.ResetToSaved();
        Assert.Equal(new ReShadeChoice("Base.ini", true), _applied.Last());
    }

    // Review fix (qa minor): what a preset FILE may name — a plain ".ini" file name that is valid on Windows and Linux.
    [Theory]
    [InlineData("Noir.txt")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a:b.ini")]
    [InlineData("what?.ini")]
    [InlineData("CON.ini")]
    [InlineData(" .ini")]
    [InlineData("")]
    public void A_preset_file_name_that_is_not_a_safe_ini_drops_the_choice_of_preset(string name)
    {
        var back = JsonSerializer.Deserialize<PresetDto>("{\"Version\":1,\"Name\":\"X\",\"ReShade\":{\"Preset\":" +
            JsonSerializer.Serialize(name) + ",\"Enabled\":false}}")!;
        Assert.Equal(new ReShadeChoice(null, false), back.ToReShade());
    }

    [Theory]
    [InlineData("Noir.ini")]
    [InlineData("Golden hour (v2).INI")]
    [InlineData("夕焼け.ini")]
    public void A_safe_ini_file_name_is_kept(string name)
    {
        var dto = PresetDto.From("X", new LookSettings(), reshade: new ReShadeChoice(name, true));
        Assert.Equal(new ReShadeChoice(name, true), dto.ToReShade());
    }

    // Review fix (qa major): ReShade's OWN preset (outside Photo Studio's presets folder) is not a file the look can find
    // again on another PC — the file keeps only on/off (the look then leaves ReShade's preset alone), never a bare name
    // that would re-open as an empty preset in our folder. Our folder's full path is stored as its file name.
    [Fact]
    public void Saving_keeps_our_folders_preset_as_a_file_name_and_drops_reshades_own()
    {
        var store = new PresetStore(_files, _ => { });
        store.Save("Ours", new LookSettings(), reshade: new ReShadeChoice(@"C:\g\stellar\plugindata\stellar.photostudio.data\reshade\presets\Noir.ini", true));
        store.Save("Theirs", new LookSettings(), reshade: new ReShadeChoice(@"C:\g\ReShadePreset.ini", false));
        var reloaded = new PresetStore(_files, _ => { });
        Assert.Equal(new ReShadeChoice("Noir.ini", true), reloaded.All.Single(p => p.Name == "Ours").ReShade);
        Assert.Equal(new ReShadeChoice(null, false), reloaded.All.Single(p => p.Name == "Theirs").ReShade);
        // and in memory, the same as on disk (no session-only difference after a relaunch)
        Assert.Equal(new ReShadeChoice(null, false), store.All.Single(p => p.Name == "Theirs").ReShade);
    }

    [Fact]
    public void A_1_5_0_preset_read_by_the_1_4_0_model_ignores_the_new_key()
    {
        var json = JsonSerializer.Serialize(PresetDto.From("Mine", new LookSettings { Color = new ColorLook { Saturation = -10f } },
            shape: PhotoShapes.TryParse("screen"), reshade: new ReShadeChoice("Noir.ini", true)));

        var legacy = JsonSerializer.Deserialize<LegacyPresetDto>(json)!;

        Assert.Equal("Mine", legacy.Name);
        Assert.Equal("screen", legacy.Shape);
        Assert.NotNull(legacy.Color);
        Assert.Equal(-10f, legacy.Color![2]);   // [PostExposure, Contrast, Saturation, R, G, B]
    }
}
