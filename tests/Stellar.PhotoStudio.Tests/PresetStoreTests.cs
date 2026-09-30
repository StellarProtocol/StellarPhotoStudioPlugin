using System;
using System.Collections.Generic;
using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Presets;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class PresetStoreTests
{
    private sealed class MemFiles : IPresetFiles
    {
        public readonly Dictionary<string, string> Files = new();
        public IEnumerable<string> List() => Files.Keys.ToList();
        public string? Read(string n) => Files.TryGetValue(n, out var j) ? j : null;
        public void Write(string n, string j) => Files[n] = j;
        public void Delete(string n) => Files.Remove(n);
    }

    [Fact]
    public void Built_ins_come_first_and_are_read_only()
    {
        var s = new PresetStore(new MemFiles(), _ => { });
        Assert.Equal(new[] { "Natural", "Cinematic", "Warm Dusk", "Noir", "Film" }, s.All.Take(5).Select(p => p.Name));
        Assert.Throws<InvalidOperationException>(() => s.Delete("Noir"));
        Assert.Throws<InvalidOperationException>(() => s.Save("Noir", new LookSettings()));
    }

    [Fact]
    public void Save_round_trips_through_json()
    {
        var files = new MemFiles();
        new PresetStore(files, _ => { }).Save("Mine", new LookSettings { Color = new ColorLook { Saturation = -12, Filter = new RgbColor(1, 0.9f, 0.8f) }, Lut = new LutLook { FilePath = "a.png", Contribution = 0.5f } });
        var again = new PresetStore(files, _ => { }).All.Single(p => p.Name == "Mine");
        Assert.Equal(-12, again.Look.Color!.Saturation);
        Assert.Equal(0.9f, again.Look.Color.Filter.G);
        Assert.Equal(0.5f, again.Look.Lut!.Contribution);
        Assert.False(again.BuiltIn);
    }

    [Fact]
    public void Corrupt_file_is_skipped_and_warned()
    {
        var files = new MemFiles();
        files.Write("Broken", "{not json");
        var warned = new List<string>();
        var s = new PresetStore(files, warned.Add);
        Assert.DoesNotContain(s.All, p => p.Name == "Broken");
        Assert.Single(warned);
    }

    [Fact]
    public void Rename_and_export_import()
    {
        var files = new MemFiles();
        var s = new PresetStore(files, _ => { });
        s.Save("A", new LookSettings { Bloom = new BloomLook { Intensity = 2 } });
        s.Rename("A", "B");
        Assert.DoesNotContain(s.All, p => p.Name == "A");
        var json = s.Export("B");
        var imported = new PresetStore(new MemFiles(), _ => { }).Import(json);
        Assert.Equal(2, imported!.Look.Bloom!.Intensity);
    }

    [Fact]
    public void Rename_to_the_same_name_is_a_noop()
    {
        var files = new MemFiles();
        var s = new PresetStore(files, _ => { });
        s.Save("A", new LookSettings { Bloom = new BloomLook { Intensity = 3 } });
        s.Rename("A", "A");
        Assert.Single(s.All, p => p.Name == "A");
        Assert.Equal(3, s.All.Single(p => p.Name == "A").Look.Bloom!.Intensity);
    }

    [Fact]
    public void Rename_to_the_same_name_in_a_different_case_is_a_noop()
    {
        var files = new MemFiles();
        var s = new PresetStore(files, _ => { });
        s.Save("Sunset", new LookSettings { Bloom = new BloomLook { Intensity = 4 } });
        s.Rename("Sunset", "sunset");
        // Case-preserving no-op: still exactly one entry, under its original casing.
        Assert.Single(s.All, p => p.Name == "Sunset");
        Assert.Equal(4, s.All.Single(p => p.Name == "Sunset").Look.Bloom!.Intensity);
    }

    [Fact]
    public void Rename_to_an_existing_different_user_preset_throws_and_changes_nothing()
    {
        var files = new MemFiles();
        var s = new PresetStore(files, _ => { });
        s.Save("A", new LookSettings { Bloom = new BloomLook { Intensity = 1 } });
        s.Save("B", new LookSettings { Bloom = new BloomLook { Intensity = 2 } });

        var ex = Assert.Throws<InvalidOperationException>(() => s.Rename("A", "B"));

        Assert.Equal("A preset with that name already exists.", ex.Message);
        Assert.Equal(1, s.All.Single(p => p.Name == "A").Look.Bloom!.Intensity);
        Assert.Equal(2, s.All.Single(p => p.Name == "B").Look.Bloom!.Intensity);
    }

    [Fact]
    public void Save_of_an_existing_user_preset_is_a_legitimate_overwrite()
    {
        var files = new MemFiles();
        var s = new PresetStore(files, _ => { });
        s.Save("Mine", new LookSettings { Bloom = new BloomLook { Intensity = 1 } });
        s.Save("Mine", new LookSettings { Bloom = new BloomLook { Intensity = 9 } });

        Assert.Single(s.All, p => p.Name == "Mine");
        Assert.Equal(9, s.All.Single(p => p.Name == "Mine").Look.Bloom!.Intensity);
    }

    [Fact]
    public void Save_matches_an_existing_user_preset_case_insensitively()
    {
        var files = new MemFiles();
        var s = new PresetStore(files, _ => { });
        s.Save("Mine", new LookSettings { Bloom = new BloomLook { Intensity = 1 } });
        s.Save("mine", new LookSettings { Bloom = new BloomLook { Intensity = 9 } });

        Assert.Single(s.All, p => string.Equals(p.Name, "mine", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(9, s.All.Single(p => string.Equals(p.Name, "mine", StringComparison.OrdinalIgnoreCase)).Look.Bloom!.Intensity);
    }

    [Fact]
    public void Save_and_delete_guard_builtin_names_case_insensitively()
    {
        var s = new PresetStore(new MemFiles(), _ => { });
        Assert.Throws<InvalidOperationException>(() => s.Save("noir", new LookSettings()));
        Assert.Throws<InvalidOperationException>(() => s.Delete("NOIR"));
    }

    [Fact]
    public void Import_auto_suffixes_on_collision_with_an_existing_user_preset()
    {
        var files = new MemFiles();
        var s = new PresetStore(files, _ => { });
        s.Save("Mine", new LookSettings());

        var first = s.Import("{\"Name\":\"Mine\"}");
        var second = s.Import("{\"Name\":\"Mine\"}");
        var third = s.Import("{\"Name\":\"Mine\"}");

        Assert.Equal("Mine (imported)", first!.Name);
        Assert.Equal("Mine (imported 2)", second!.Name);
        Assert.Equal("Mine (imported 3)", third!.Name);
    }

    [Fact]
    public void Import_auto_suffixes_on_collision_with_a_builtin_name()
    {
        var s = new PresetStore(new MemFiles(), _ => { });
        var imported = s.Import("{\"Name\":\"Noir\"}");
        Assert.Equal("Noir (imported)", imported!.Name);
    }

    [Fact]
    public void Load_skips_an_ondisk_preset_colliding_with_a_builtin_name_and_warns()
    {
        var files = new MemFiles();
        files.Write("noir", "{\"Name\":\"noir\"}");
        var warned = new List<string>();

        var s = new PresetStore(files, warned.Add);

        Assert.DoesNotContain(s.All, p => p.Name == "noir");
        Assert.Single(warned);
        Assert.Contains(s.All, p => p.Name == "Noir" && p.BuiltIn);
    }

    [Fact]
    public void All_is_cached_between_reads()
    {
        var s = new PresetStore(new MemFiles(), _ => { });
        Assert.Same(s.All, s.All);
    }

    [Fact]
    public void All_cache_is_invalidated_on_mutation()
    {
        var s = new PresetStore(new MemFiles(), _ => { });
        var before = s.All;
        s.Save("New", new LookSettings());
        var after = s.All;

        Assert.NotSame(before, after);
        Assert.Contains(after, p => p.Name == "New");
    }

    // Fix round 2: the in-memory identity match is case-insensitive, but Save/Delete must keep the
    // UNDERLYING STORAGE keyed consistently too — otherwise a case-only re-save leaves a stale file
    // behind, and a cross-case delete leaves the original file (and preset) alive across a restart.
    // "Restart" is modeled by opening a second PresetStore over the same backing MemFiles.

    [Fact]
    public void Case_only_resave_leaves_exactly_one_file_and_one_preset_after_restart()
    {
        var files = new MemFiles();
        var s1 = new PresetStore(files, _ => { });
        s1.Save("Mine", new LookSettings { Bloom = new BloomLook { Intensity = 1 } });
        s1.Save("mine", new LookSettings { Bloom = new BloomLook { Intensity = 9 } });

        Assert.Single(files.Files); // no stale "Mine" file left behind alongside "mine"

        var s2 = new PresetStore(files, _ => { }); // restart
        Assert.Single(s2.All, p => string.Equals(p.Name, "mine", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(9, s2.All.Single(p => string.Equals(p.Name, "mine", StringComparison.OrdinalIgnoreCase)).Look.Bloom!.Intensity);
    }

    [Fact]
    public void Cross_case_delete_removes_the_stored_file_and_stays_gone_after_restart()
    {
        var files = new MemFiles();
        var s1 = new PresetStore(files, _ => { });
        s1.Save("Mine", new LookSettings());
        s1.Delete("mine"); // different case than the tracked/stored entry

        Assert.Empty(files.Files);
        Assert.DoesNotContain(s1.All, p => string.Equals(p.Name, "mine", StringComparison.OrdinalIgnoreCase));

        var s2 = new PresetStore(files, _ => { }); // restart — must not resurrect it
        Assert.DoesNotContain(s2.All, p => string.Equals(p.Name, "mine", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Load_dedupes_ondisk_case_variants_keeping_the_first_in_ordinal_order_and_warns()
    {
        var seed = new MemFiles();
        new PresetStore(seed, _ => { }).Save("Mine", new LookSettings { Bloom = new BloomLook { Intensity = 5 } });
        var json = seed.Files["Mine"];

        var files = new MemFiles();
        files.Write("Mine", json);  // ordinal 'M' (77) < 'm' (109) — "Mine" sorts first
        files.Write("mine", json);
        var warned = new List<string>();

        var s = new PresetStore(files, warned.Add);

        Assert.Single(s.All, p => string.Equals(p.Name, "Mine", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Mine", s.All.Single(p => string.Equals(p.Name, "Mine", StringComparison.OrdinalIgnoreCase)).Name);
        Assert.Single(warned);
    }
}
