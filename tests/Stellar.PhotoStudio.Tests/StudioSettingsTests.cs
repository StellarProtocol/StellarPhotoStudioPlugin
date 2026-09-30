using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

public sealed class StudioSettingsTests
{
    private sealed class MemSection : IConfigSection
    {
        public readonly Dictionary<string, object?> Values = new();
        public int Saves;
        public T? Get<T>(string key, T? defaultValue) => Values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        public void Set<T>(string key, T value) => Values[key] = value;
        public void Save() => Saves++;
        public void SaveQuiet() => Saves++;
        public void RemoveByPrefix(string prefix) { }
    }

    [Fact]
    public void Defaults_match_the_spec()
    {
        var s = new StudioSettings(new MemSection());
        Assert.Equal(2, s.Scale);
        Assert.Equal(CaptureFormat.Png, s.Format);
        Assert.Equal(92, s.JpgQuality);
        Assert.Equal("", s.Folder);
        Assert.True(s.DockedAuto);
        Assert.Equal(LookGroups.Color, s.OpenGroups);
    }

    [Fact]
    public void Stellar_overlay_hide_is_never_persisted()
    {
        var cfg = new MemSection();
        new StudioSettings(cfg).SetHides(VisibilityLayers.StellarOverlay | VisibilityLayers.Nameplates);
        var again = new StudioSettings(cfg);
        Assert.Equal(VisibilityLayers.Nameplates, again.Hides);
    }

    [Fact]
    public void Invalid_scale_and_quality_are_normalized()
    {
        var cfg = new MemSection();
        cfg.Values["capture.scale"] = 3;
        cfg.Values["capture.jpgQuality"] = 400;
        var s = new StudioSettings(cfg);
        Assert.Equal(2, s.Scale);
        Assert.Equal(100, s.JpgQuality);
    }

    [Fact]
    public void Setters_round_trip_and_save()
    {
        var cfg = new MemSection();
        var s = new StudioSettings(cfg);
        s.SetScale(4); s.SetFormat(CaptureFormat.Jpg); s.SetFolder("  /pics  "); s.SetPinned(true);
        s.SetGroupOpen(LookGroups.Dof, true); s.SetGroupOpen(LookGroups.Color, false);
        var again = new StudioSettings(cfg);
        Assert.Equal(4, again.Scale);
        Assert.Equal(CaptureFormat.Jpg, again.Format);
        Assert.Equal("/pics", again.Folder);
        Assert.True(again.Pinned);
        Assert.Equal(LookGroups.Dof, again.OpenGroups);
        Assert.True(cfg.Saves >= 6);
    }
}
