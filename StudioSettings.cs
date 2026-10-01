using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// The panel's persisted choices (config section <c>photostudio</c>). The Stellar-overlay hide is deliberately
/// never stored: a relaunch must never start with an invisible panel. Every setter saves immediately.
/// </summary>
internal sealed class StudioSettings
{
    private const VisibilityLayers Persistable =
        VisibilityLayers.GameHud | VisibilityLayers.Nameplates | VisibilityLayers.OtherPlayers | VisibilityLayers.KeepParty;

    private readonly IConfigSection _cfg;

    public StudioSettings(IConfigSection cfg)
    {
        _cfg = cfg;
        Scale = Normalize(cfg.Get("capture.scale", 2));
        Format = cfg.Get("capture.format", "png") == "jpg" ? CaptureFormat.Jpg : CaptureFormat.Png;
        JpgQuality = Math.Clamp(cfg.Get("capture.jpgQuality", 92), 1, 100);
        Folder = cfg.Get("capture.folder", "") ?? "";
        Hides = (VisibilityLayers)cfg.Get("hide.layers", 0) & Persistable;
        Pinned = cfg.Get("look.pinned", false);
        PresetName = cfg.Get("look.presetName", "Natural") ?? "Natural";
        WorkingJson = cfg.Get<string?>("look.working", null);
        Tab = ReadTab(cfg);
        OpenGroups = (LookGroups)cfg.Get("ui.openGroups", (int)LookGroups.Color);
        DockedAuto = cfg.Get("docked.auto", true);
        Supersample = Mode(cfg.Get("quality.supersample", 0));
        Shadows = Mode(cfg.Get("quality.shadows", 0));
        BoostForCapture = cfg.Get("quality.boostCapture", true);
        TimeMode = Mode(cfg.Get("time.mode", 0));
        TimeHour = Math.Clamp(cfg.Get("time.hour", 12f), 0f, 24f);
        QualityOpen = cfg.Get("ui.qualityOpen", true);
    }

    public QualityMode Supersample { get; private set; }
    public QualityMode Shadows { get; private set; }
    public bool BoostForCapture { get; private set; }
    public QualityMode TimeMode { get; private set; }
    public float TimeHour { get; private set; }
    public bool QualityOpen { get; private set; }

    public void SetSupersample(QualityMode m) { Supersample = m; Store("quality.supersample", (int)m); }
    public void SetShadows(QualityMode m) { Shadows = m; Store("quality.shadows", (int)m); }
    public void SetBoostForCapture(bool on) { BoostForCapture = on; Store("quality.boostCapture", on); }
    public void SetTimeMode(QualityMode m) { TimeMode = m; Store("time.mode", (int)m); }
    public void SetQualityOpen(bool open) { QualityOpen = open; Store("ui.qualityOpen", open); }

    /// <summary>The hour slider fires every frame while dragged: <paramref name="save"/> false keeps it in memory
    /// only, and the owner saves once the drag settles (a config save is a main-thread file write).</summary>
    public void SetTimeHour(float h, bool save)
    {
        TimeHour = Math.Clamp(h, 0f, 24f);
        if (save) Store("time.hour", TimeHour);
    }

    private static QualityMode Mode(int v) => v is >= 0 and <= 2 ? (QualityMode)v : QualityMode.Off;

    public int Scale { get; private set; }
    public CaptureFormat Format { get; private set; }
    public int JpgQuality { get; private set; }
    /// <summary>Custom screenshot folder; empty = the default folder.</summary>
    public string Folder { get; private set; }
    /// <summary>Remembered hide toggles (never includes the Stellar overlay).</summary>
    public VisibilityLayers Hides { get; private set; }
    public bool Pinned { get; private set; }
    public string PresetName { get; private set; }
    /// <summary>The unsaved working look (a preset JSON), so edits survive a relaunch; null = none.</summary>
    public string? WorkingJson { get; private set; }
    public int Tab { get; private set; }
    public LookGroups OpenGroups { get; private set; }
    public bool DockedAuto { get; private set; }

    public void SetScale(int s) { Scale = Normalize(s); Store("capture.scale", Scale); }
    public void SetFormat(CaptureFormat f) { Format = f; Store("capture.format", f == CaptureFormat.Jpg ? "jpg" : "png"); }
    public void SetJpgQuality(int q) { JpgQuality = Math.Clamp(q, 1, 100); Store("capture.jpgQuality", JpgQuality); }
    public void SetFolder(string f) { Folder = f.Trim(); Store("capture.folder", Folder); }
    public void SetHides(VisibilityLayers h) { Hides = h & Persistable; Store("hide.layers", (int)Hides); }
    public void SetPinned(bool p) { Pinned = p; Store("look.pinned", p); }
    public void SetPresetName(string n) { PresetName = n; Store("look.presetName", n); }
    public void SetWorkingJson(string? j) { WorkingJson = j; Store("look.working", j); }
    // 1.1.0 stores the tab under "ui.tab2" (0..3) and never writes "ui.tab", so a 1.0.0 rollback keeps its own value.
    public void SetTab(int t) { Tab = Math.Clamp(t, StudioTabs.Capture, StudioTabs.Presets); Store("ui.tab2", Tab); }

    private static int ReadTab(IConfigSection cfg)
    {
        var current = cfg.Get("ui.tab2", -1);
        if (current >= 0) return Math.Clamp(current, StudioTabs.Capture, StudioTabs.Presets);
        var old = Math.Clamp(cfg.Get("ui.tab", 0), 0, 2);   // 1.0.0: 0 Capture, 1 Look, 2 Presets
        return old == 2 ? StudioTabs.Presets : old;
    }

    public void SetGroupOpen(LookGroups g, bool open)
    {
        OpenGroups = open ? OpenGroups | g : OpenGroups & ~g;
        Store("ui.openGroups", (int)OpenGroups);
    }
    public void SetDockedAuto(bool a) { DockedAuto = a; Store("docked.auto", a); }

    private void Store<T>(string key, T value)
    {
        _cfg.Set(key, value);
        _cfg.SaveQuiet();
    }

    private static int Normalize(int s) => s is 1 or 2 or 4 ? s : 2;
}

/// <summary>Panel tab indices (1.1.0 order: Capture · Look · Camera · Presets).</summary>
internal static class StudioTabs
{
    public const int Capture = 0, Look = 1, Camera = 2, Presets = 3;
}
