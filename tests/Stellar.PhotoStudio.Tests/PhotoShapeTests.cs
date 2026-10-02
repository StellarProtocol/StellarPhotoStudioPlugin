using System.Collections.Generic;
using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Presets;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

// Photo shapes (spec 2026-10-03-photo-studio-portrait-capture-design.md, owner-approved): settings + presets carry the
// shape, the size labels come from the framework calculator, and the guide rectangle is the photo's framing.
public sealed class PhotoShapeTests
{
    private sealed class MemSection : IConfigSection
    {
        public readonly Dictionary<string, object?> Values = new();
        public T? Get<T>(string key, T? defaultValue) => Values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        public void Set<T>(string key, T value) => Values[key] = value;
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

    // ── settings ──

    [Fact]
    public void Defaults_are_screen_and_guide_on()
    {
        var s = new StudioSettings(new MemSection());
        Assert.Equal(PhotoShape.Screen, s.Shape);
        Assert.True(s.ShowFrameGuide);
    }

    [Theory]
    [InlineData("9:16")] [InlineData("4:5")] [InlineData("2:3")]
    [InlineData("1:1")] [InlineData("21:9")] [InlineData("screen")]
    public void Shape_and_guide_round_trip_through_the_config(string key)
    {
        var shape = PhotoShapes.Parse(key);
        var cfg = new MemSection();
        var s = new StudioSettings(cfg);
        s.SetShape(shape);
        s.SetShowFrameGuide(false);
        var again = new StudioSettings(cfg);
        Assert.Equal(shape, again.Shape);
        Assert.False(again.ShowFrameGuide);
    }

    [Fact]
    public void Shape_is_stored_as_its_ratio_key_and_an_unknown_key_reads_as_screen()
    {
        var cfg = new MemSection();
        new StudioSettings(cfg).SetShape(PhotoShape.Portrait9x16);
        Assert.Equal("9:16", cfg.Values["capture.shape"]);
        cfg.Values["capture.shape"] = "3:7";   // a newer build's shape
        Assert.Equal(PhotoShape.Screen, new StudioSettings(cfg).Shape);
    }

    [Fact]
    public void Every_shape_key_parses_back()
    {
        foreach (var s in PhotoShapes.All) Assert.Equal(s, PhotoShapes.Parse(PhotoShapes.Key(s)));
        Assert.Equal(new[] { "screen", "9:16", "4:5", "2:3", "1:1", "21:9" }, PhotoShapes.All.Select(PhotoShapes.Key));
        Assert.Null(PhotoShapes.Aspect(PhotoShape.Screen));
        Assert.Equal(new CaptureAspect(21, 9), PhotoShapes.Aspect(PhotoShape.Wide21x9));
    }

    [Fact]
    public void Capture_request_carries_the_aspect()
    {
        var r = CaptureController.BuildRequest(new CaptureSettings(2, CaptureFormat.Png, 92, null, 0, new CaptureAspect(9, 16)), System.DateTime.Now, "/d");
        Assert.Equal(new CaptureAspect(9, 16), r.Aspect);
        Assert.Null(CaptureController.BuildRequest(new CaptureSettings(2, CaptureFormat.Png, 92, null, 0), System.DateTime.Now, "/d").Aspect);
    }

    // ── presets ──

    [Fact]
    public void Preset_shape_round_trips_through_files_export_import_and_rename()
    {
        var files = new MemFiles();
        var store = new PresetStore(files, _ => { });
        store.Save("Portrait", new LookSettings(), PhotoShape.Portrait4x5);
        Assert.Equal(PhotoShape.Portrait4x5, new PresetStore(files, _ => { }).All.Single(p => p.Name == "Portrait").Shape);
        var imported = new PresetStore(new MemFiles(), _ => { }).Import(store.Export("Portrait"));
        Assert.Equal(PhotoShape.Portrait4x5, imported!.Shape);
        store.Rename("Portrait", "Tall");
        Assert.Equal(PhotoShape.Portrait4x5, new PresetStore(files, _ => { }).All.Single(p => p.Name == "Tall").Shape);
    }

    [Fact]
    public void A_preset_file_without_a_shape_sets_none()
    {
        var files = new MemFiles();
        files.Files["Old"] = "{\"Version\":1,\"Name\":\"Old\"}";
        files.Files["Future"] = "{\"Version\":1,\"Name\":\"Future\",\"Shape\":\"3:7\"}";
        var store = new PresetStore(files, _ => { });
        Assert.Null(store.All.Single(p => p.Name == "Old").Shape);
        Assert.Null(store.All.Single(p => p.Name == "Future").Shape);
        Assert.All(store.All.Where(p => p.BuiltIn), p => Assert.Null(p.Shape));
    }

    [Fact]
    public void Session_saves_the_current_shape_and_applying_sets_it_only_when_the_preset_has_one()
    {
        var store = new PresetStore(new MemFiles(), _ => { });
        var current = PhotoShape.Square;
        var applied = new List<PhotoShape>();
        var session = new PresetSession(store, new LookEditor(), "Natural", null,
            new PresetShapeLink(() => current, s => { applied.Add(s); current = s; }));
        session.SaveAs("Sq");
        Assert.Equal(PhotoShape.Square, store.All.Single(p => p.Name == "Sq").Shape);
        current = PhotoShape.Wide21x9;
        session.Apply(store.All.Single(p => p.Name == "Natural"));   // built-in: no shape
        Assert.Empty(applied);
        session.Apply(store.All.Single(p => p.Name == "Sq"));
        Assert.Equal(new[] { PhotoShape.Square }, applied);
        current = PhotoShape.Portrait2x3;
        session.Save();
        Assert.Equal(PhotoShape.Portrait2x3, store.All.Single(p => p.Name == "Sq").Shape);
    }

    // ── size labels (spec table: 2× on 1920 × 1080) ──

    [Theory]
    [InlineData("9:16", "2160 × 3840", "2× · PNG · 2160 × 3840 (9:16)", "9:16 · 2160 × 3840")]
    [InlineData("4:5", "3072 × 3840", "2× · PNG · 3072 × 3840 (4:5)", "4:5 · 3072 × 3840")]
    [InlineData("2:3", "2560 × 3840", "2× · PNG · 2560 × 3840 (2:3)", "2:3 · 2560 × 3840")]
    [InlineData("1:1", "3840 × 3840", "2× · PNG · 3840 × 3840 (1:1)", "1:1 · 3840 × 3840")]
    [InlineData("21:9", "3840 × 1646", "2× · PNG · 3840 × 1646 (21:9)", "21:9 · 3840 × 1646")]
    [InlineData("screen", "3840 × 2160", "2× · PNG · 3840 × 2160", "")]
    public void Size_labels_from_the_calculator(string key, string size, string status, string guide)
    {
        var shape = PhotoShapes.Parse(key);
        var s = ShapeFrame.OutputSize(shape, 1920, 1080, 2);
        Assert.Equal(size, ShapeFrame.SizeText(s));
        Assert.Equal(status, ShapeFrame.StatusText(2, "PNG", s, shape));
        Assert.Equal(guide, ShapeFrame.GuideLabel(shape, s));
    }

    [Fact]
    public void Four_x_label_doubles_each_side() =>
        Assert.Equal("4× · JPG · 4320 × 7680 (9:16)", ShapeFrame.StatusText(4, "JPG", ShapeFrame.OutputSize(PhotoShape.Portrait9x16, 1920, 1080, 4), PhotoShape.Portrait9x16));

    // ── guide rectangle (normalized, top-left origin) on 16:9 and 16:10 ──

    [Theory]
    // 16:9 — 1920 × 1080: narrower shapes are full height, 21:9 is full width.
    [InlineData(1920, 1080, "9:16", 0.341797f, 0f, 0.316406f, 1f)]
    [InlineData(1920, 1080, "4:5", 0.275000f, 0f, 0.450000f, 1f)]
    [InlineData(1920, 1080, "2:3", 0.312500f, 0f, 0.375000f, 1f)]
    [InlineData(1920, 1080, "1:1", 0.218750f, 0f, 0.562500f, 1f)]
    [InlineData(1920, 1080, "21:9", 0f, 0.119048f, 1f, 0.761905f)]
    [InlineData(1920, 1080, "screen", 0f, 0f, 1f, 1f)]
    // 16:10 — 1920 × 1200.
    [InlineData(1920, 1200, "9:16", 0.324219f, 0f, 0.351563f, 1f)]
    [InlineData(1920, 1200, "4:5", 0.250000f, 0f, 0.500000f, 1f)]
    [InlineData(1920, 1200, "2:3", 0.291667f, 0f, 0.416667f, 1f)]
    [InlineData(1920, 1200, "1:1", 0.187500f, 0f, 0.625000f, 1f)]
    [InlineData(1920, 1200, "21:9", 0f, 0.157143f, 1f, 0.685714f)]
    [InlineData(1920, 1200, "screen", 0f, 0f, 1f, 1f)]
    public void Guide_rect_for_each_shape(int w, int h, string key, float x, float y, float rw, float rh)
    {
        var shape = PhotoShapes.Parse(key);
        var r = ShapeFrame.GuideRect(shape, w, h);
        Assert.Equal(x, r.X, 4); Assert.Equal(y, r.Y, 4); Assert.Equal(rw, r.Width, 4); Assert.Equal(rh, r.Height, 4);
        // the rectangle in pixels has the shape's own proportions, centred
        if (PhotoShapes.Aspect(shape) is { } a) Assert.Equal(a.Ratio, r.Width * w / (r.Height * h), 3);
        Assert.Equal(1f, r.X * 2f + r.Width, 4);
        Assert.Equal(1f, r.Y * 2f + r.Height, 4);
    }

    [Fact]
    public void Thirds_split_the_guide_in_three()
    {
        var t = ShapeFrame.Thirds(new NormalizedRect(0.25f, 0f, 0.5f, 1f));
        Assert.Equal((0.25f + 0.5f / 3f, 0.25f + 1f / 3f, 1f / 3f, 2f / 3f), (t.X1, t.X2, t.Y1, t.Y2));
    }

    [Theory]
    [InlineData("9:16", true, true, false, true)]
    [InlineData("9:16", true, false, true, true)]
    [InlineData("9:16", true, false, false, false)]
    [InlineData("9:16", false, true, true, false)]
    [InlineData("screen", true, true, true, false)]
    public void Guide_shows_while_framing_a_shape_with_the_toggle_on(string key, bool toggle, bool cam, bool tab, bool visible) =>
        Assert.Equal(visible, ShapeFrame.GuideVisible(PhotoShapes.Parse(key), toggle, cam, tab));

    // ── toast shortfall + sidecar scale ──

    [Fact]
    public void Shortfall_tells_capped_from_retried()
    {
        var c = new CaptureSize(1, 1);
        Assert.Equal(CaptureShortfall.None, ShapeFrame.Shortfall(PhotoShape.Portrait9x16, new CaptureSize(2160, 3840), new CaptureSize(2160, 3840), 1920, 2));
        Assert.Equal(CaptureShortfall.Retried2x, ShapeFrame.Shortfall(PhotoShape.Portrait9x16, new CaptureSize(2160, 3840), new CaptureSize(4320, 7680), 1920, 4));
        Assert.Equal(CaptureShortfall.ShapeCapped, ShapeFrame.Shortfall(PhotoShape.Square, new CaptureSize(8000, 8000), new CaptureSize(8000, 8000), 3840, 4));
        Assert.Equal(CaptureShortfall.ScaleCapped, ShapeFrame.Shortfall(PhotoShape.Screen, new CaptureSize(7680, 4320), new CaptureSize(7680, 4320), 3840, 4));
        Assert.Equal(CaptureShortfall.None, ShapeFrame.Shortfall(PhotoShape.Screen, new CaptureSize(0, 0), c, 3840, 4));
    }

    [Fact]
    public void Sidecar_scale_reads_the_long_side() =>
        Assert.Equal(2, ShapeFrame.CapturedScale(new CaptureSize(2160, 3840), 1920, 1080, 1));
}
