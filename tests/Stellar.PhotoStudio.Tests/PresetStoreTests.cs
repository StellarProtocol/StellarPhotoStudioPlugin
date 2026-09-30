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
}
