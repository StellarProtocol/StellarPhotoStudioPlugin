using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

/// <summary>
/// The panel's persisted choices (config section <c>photostudio</c>). The Stellar-overlay hide is deliberately
/// never stored: a relaunch must never start with an invisible panel. Every setter saves immediately.
/// </summary>
internal sealed class StudioSettings
{
    private const VisibilityLayers Persistable =
        VisibilityLayers.GameHud | VisibilityLayers.Nameplates | VisibilityLayers.OtherPlayers | VisibilityLayers.KeepParty |
        VisibilityLayers.Self | VisibilityLayerSets.Effects;

    private readonly IConfigSection _cfg;

    public StudioSettings(IConfigSection cfg)
    {
        _cfg = cfg;
        Scale = Normalize(cfg.Get("capture.scale", 2));
        Format = cfg.Get("capture.format", "png") == "jpg" ? CaptureFormat.Jpg : CaptureFormat.Png;
        JpgQuality = Math.Clamp(cfg.Get("capture.jpgQuality", 92), 1, 100);
        Folder = cfg.Get("capture.folder", "") ?? "";
        Shape = PhotoShapes.Parse(cfg.Get("capture.shape", "screen"));
        ShowFrameGuide = cfg.Get("capture.frameGuide", true);
        Hides = (VisibilityLayers)cfg.Get("hide.layers", 0) & Persistable;
        Pinned = cfg.Get("look.pinned", false);
        PresetName = cfg.Get("look.presetName", "Natural") ?? "Natural";
        WorkingJson = cfg.Get<string?>("look.working", null);
        Tab = ReadTab(cfg);
        LampsOpen = cfg.Get("ui.lights.lampsOpen", true);
        ShowLampMarkers = cfg.Get("lights.markers", true);
        PersonLightOpen = cfg.Get("ui.lights.personOpen", true);
        PeopleLevel = ClampLevel(cfg.Get("lights.peopleLevel", Lights.LightsController.DefaultPeopleLevel));
        OpenGroups = (LookGroups)cfg.Get("ui.openGroups", (int)LookGroups.Color);
        DockedAuto = cfg.Get("docked.auto", true);
        Supersample = Mode(cfg.Get("quality.supersample", 0));
        Shadows = Mode(cfg.Get("quality.shadows", 0));
        BoostForCapture = cfg.Get("quality.boostCapture", true);
        TimeMode = Mode(cfg.Get("time.mode", 0));
        TimeHour = Math.Clamp(cfg.Get("time.hour", 12f), 0f, 24f);
        QualityOpen = cfg.Get("ui.qualityOpen", true);
        ReShadeOpen = cfg.Get("ui.reshade.open", true);
        PacksOpen = cfg.Get("ui.reshade.packsOpen", true);
        AllFxOpen = cfg.Get("ui.reshade.allFx", false);
        PresetsOpen = cfg.Get("ui.reshade.presetsOpen", true);
        AimStepIndex = Math.Clamp(cfg.Get("pose.aimStep", 1), 0, 2);
        for (var k = 0; k < _presetGroups.Length; k++) _presetGroups[k] = cfg.Get(PresetGroupKey((PresetKind)k), k == (int)PresetKind.Own);
    }

    public QualityMode Supersample { get; private set; }
    public QualityMode Shadows { get; private set; }
    public bool BoostForCapture { get; private set; }
    public QualityMode TimeMode { get; private set; }
    public float TimeHour { get; private set; }
    public bool QualityOpen { get; private set; }
    public bool ReShadeOpen { get; private set; }
    public bool PacksOpen { get; private set; }
    public bool AllFxOpen { get; private set; }
    public bool PresetsOpen { get; private set; }
    /// <summary>Head/Eyes arrow-pad step: 0 Fine, 1 Normal (default), 2 Coarse (PosingController.AimSteps).</summary>
    public int AimStepIndex { get; private set; }
    private readonly bool[] _presetGroups = new bool[3];

    /// <summary>Presets group folds: Photo Studio's own open, Community and author-page links folded by default.</summary>
    public bool PresetGroupOpen(PresetKind kind) => _presetGroups[(int)kind];

    public void SetSupersample(QualityMode m) { Supersample = m; Store("quality.supersample", (int)m); }
    public void SetShadows(QualityMode m) { Shadows = m; Store("quality.shadows", (int)m); }
    public void SetBoostForCapture(bool on) { BoostForCapture = on; Store("quality.boostCapture", on); }
    public void SetTimeMode(QualityMode m) { TimeMode = m; Store("time.mode", (int)m); }
    public void SetQualityOpen(bool open) { QualityOpen = open; Store("ui.qualityOpen", open); }
    public void SetReShadeOpen(bool open) { ReShadeOpen = open; Store("ui.reshade.open", open); }
    public void SetPacksOpen(bool open) { PacksOpen = open; Store("ui.reshade.packsOpen", open); }
    public void SetAllFxOpen(bool open) { AllFxOpen = open; Store("ui.reshade.allFx", open); }
    public void SetPresetsOpen(bool open) { PresetsOpen = open; Store("ui.reshade.presetsOpen", open); }
    public void SetAimStepIndex(int i) { AimStepIndex = Math.Clamp(i, 0, 2); Store("pose.aimStep", AimStepIndex); }
    public void SetPresetGroupOpen(PresetKind kind, bool open) { _presetGroups[(int)kind] = open; Store(PresetGroupKey(kind), open); }

    private static string PresetGroupKey(PresetKind kind) => kind switch
    {
        PresetKind.Own => "ui.reshade.presets.ownOpen",
        PresetKind.Community => "ui.reshade.presets.communityOpen",
        _ => "ui.reshade.presets.linksOpen",
    };

    /// <summary>The hour slider fires every frame while dragged: <paramref name="save"/> false keeps it in memory
    /// only, and the owner saves once the drag settles (a config save is a main-thread file write).</summary>
    public void SetTimeHour(float h, bool save)
    {
        TimeHour = Math.Clamp(h, 0f, 24f);
        if (save) Store("time.hour", TimeHour);
    }

    private static QualityMode Mode(int v) => v is >= 0 and <= 2 ? (QualityMode)v : QualityMode.Off;

    public int Scale { get; private set; }
    /// <summary>The photo's shape (spec 2026-10-03); Screen = the window's shape (default, today's behaviour).</summary>
    public PhotoShape Shape { get; private set; }
    /// <summary>"Show frame guide" (default on): the dimmed outside-the-shape overlay while framing.</summary>
    public bool ShowFrameGuide { get; private set; }
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
    // Stored as a string key ("screen", "9:16", …): an unknown value (a newer build's shape) reads back as Screen.
    public void SetShape(PhotoShape s)
    {
        var changed = Shape != s;
        Shape = s;
        Store("capture.shape", PhotoShapes.Key(s));
        if (changed) ShapeChanged?.Invoke();
    }

    /// <summary>Raised after <see cref="Shape"/> changes value (the preset session counts it as an edit).</summary>
    public event Action? ShapeChanged;
    public void SetShowFrameGuide(bool on) { ShowFrameGuide = on; Store("capture.frameGuide", on); }
    public void SetFormat(CaptureFormat f) { Format = f; Store("capture.format", f == CaptureFormat.Jpg ? "jpg" : "png"); }
    public void SetJpgQuality(int q) { JpgQuality = Math.Clamp(q, 1, 100); Store("capture.jpgQuality", JpgQuality); }
    public void SetFolder(string f) { Folder = f.Trim(); Store("capture.folder", Folder); }
    public void SetHides(VisibilityLayers h) { Hides = h & Persistable; Store("hide.layers", (int)Hides); }
    public void SetPinned(bool p) { Pinned = p; Store("look.pinned", p); }
    public void SetPresetName(string n) { PresetName = n; Store("look.presetName", n); }
    public void SetWorkingJson(string? j) { WorkingJson = j; Store("look.working", j); }
    // 1.3.0 stores the tab under "ui.tab3" (0..4, Lights inserted at 3); "ui.tab2" (1.1.0, 0..3) and "ui.tab" (1.0.0) are
    // read once for migration and never written, so a rollback keeps its own value.
    public void SetTab(int t) { Tab = Math.Clamp(t, StudioTabs.Capture, StudioTabs.Presets); Store("ui.tab3", Tab); }

    private static int ReadTab(IConfigSection cfg)
    {
        var now = cfg.Get("ui.tab3", -1);
        if (now >= 0) return Math.Clamp(now, StudioTabs.Capture, StudioTabs.Presets);
        var v11 = cfg.Get("ui.tab2", -1);   // 1.1.0: 0 Capture, 1 Look, 2 Camera, 3 Presets
        if (v11 >= 0) return v11 >= 3 ? StudioTabs.Presets : v11;
        var old = Math.Clamp(cfg.Get("ui.tab", 0), 0, 2);   // 1.0.0: 0 Capture, 1 Look, 2 Presets
        return old == 2 ? StudioTabs.Presets : old;
    }

    public bool LampsOpen { get; private set; } = true;
    public bool ShowLampMarkers { get; private set; } = true;
    public void SetShowLampMarkers(bool on) { ShowLampMarkers = on; Store("lights.markers", on); }
    public bool PersonLightOpen { get; private set; } = true;
    /// <summary>Light people (lights review minor: persisted, a setting not a scene object). The slider fires every frame:
    /// <paramref name="save"/> false keeps it in memory and the owner saves once the drag settles.</summary>
    public float PeopleLevel { get; private set; }

    public void SetPeopleLevel(float level, bool save)
    {
        PeopleLevel = ClampLevel(level);
        if (save) Store("lights.peopleLevel", PeopleLevel);
    }

    private static float ClampLevel(float v) =>
        float.IsFinite(v) ? Math.Clamp(v, 0f, LightLimits.MaxPeopleLevel) : Lights.LightsController.DefaultPeopleLevel;

    public void SetLampsOpen(bool open) { LampsOpen = open; Store("ui.lights.lampsOpen", open); }
    public void SetPersonLightOpen(bool open) { PersonLightOpen = open; Store("ui.lights.personOpen", open); }

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

/// <summary>Panel tab indices (1.3.0 order: Capture · Look · Camera · Lights · Presets).</summary>
internal static class StudioTabs
{
    public const int Capture = 0, Look = 1, Camera = 2, Lights = 3, Presets = 4;
}
