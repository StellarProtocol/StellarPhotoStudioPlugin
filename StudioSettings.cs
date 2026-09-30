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
        Tab = Math.Clamp(cfg.Get("ui.tab", 0), 0, 2);
        OpenGroups = (LookGroups)cfg.Get("ui.openGroups", (int)LookGroups.Color);
        DockedAuto = cfg.Get("docked.auto", true);
    }

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
    public void SetTab(int t) { Tab = Math.Clamp(t, 0, 2); Store("ui.tab", Tab); }
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
