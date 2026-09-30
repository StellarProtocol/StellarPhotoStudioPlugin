using System;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Plugins;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Presets;

namespace Stellar.PhotoStudio;

/// <summary>
/// Photo Studio — hide the HUD/nameplates/other players, capture a supersampled screenshot, and
/// grade the shot with a look built from the game's own render-pipeline volumes (depth of field,
/// colour, white balance, LUT, bloom, vignette, film grain). UI, presets and orchestration only;
/// the actual capture/look/visibility mechanics live in the framework.
/// </summary>
public sealed partial class Plugin : IStellarPlugin
{
    private readonly IPluginServices _services;
    private readonly ILocalization _loc;
    private readonly LookController _look;
    private readonly LookEditor _editor = new();
    private readonly PresetStore _presets;
    private readonly StudioSettings _settings;
    private readonly StudioSession _session;
    private readonly IStudioView _view;
    private readonly PanelOpenState _panel;
    private readonly PhotoModeAttach _photoModeAttach;
    private readonly Action<float> _onFrameworkUpdate;
    private readonly Action<bool> _onCutsceneChanged;
    private readonly Action _onLanguageChanged;

    private IDisposable? _hideAllToken;
    private readonly string _screenshotFolder;
    private readonly string _studioFolder;

    public Plugin(IPluginServices services)
    {
        _services = services;
        _loc = services.Localization;
        LogBootDiag(); // Plugin.Diagnostics.cs — gated on StellarDiagnostics.IsEnabled

        var assemblyDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
        var root = GameRootLocator.Resolve(AppContext.BaseDirectory, assemblyDir, Directory.Exists);
        _screenshotFolder = Path.Combine(root.Path, "stellar", "screenshots");
        _studioFolder = Path.Combine(root.Path, "stellar", "photostudio");
        // Not diagnostic spam — a plain, always-on boot line so the resolved path is visible in a normal log.
        services.Log.Info($"[PhotoStudio] game root resolved: {root.Path} (verified={root.Verified})");

        _settings = new StudioSettings(services.Config.GetSection("photostudio"));
        _look = new LookController(services.RenderLook);
        _look.SetPinned(_settings.Pinned);
        _presets = new PresetStore(new DataStorePresetFiles(services.Data), m => services.Log.Warning(m));
        StartPresetSession();                    // Plugin.Studio.cs
        _editor.Changed += OnEditorChanged;

        _session = new StudioSession(services.ScreenCapture, BuildRequest, OnCaptureResult, services.Log.Warning);
        RegisterWindows();                       // Plugin.Studio.cs — panel, docked strip, toast, tip, flash
        _view = new WindowStudioView(this);
        _panel = new PanelOpenState(_view, _look);

        DeclareHotkeys();

        _photoModeAttach = new PhotoModeAttach(services.PhotoMode, SetDockedForGamePhotoMode);
        _onCutsceneChanged = suspended => _look.SetSuspended(suspended);
        services.PhotoMode.CutsceneChanged += _onCutsceneChanged;
        // A (re)load mid-cutscene or inside the game's photo mode must start in the right state, not wait for a change.
        _look.SetSuspended(services.PhotoMode.InCutscene);
        SetDockedForGamePhotoMode(services.PhotoMode.IsActive);

        _onFrameworkUpdate = OnUpdate;
        services.Framework.Update += _onFrameworkUpdate;
        _onLanguageChanged = () => { _lutOptionsCache = null; _importOptionsCache = null; };
        _loc.LanguageChanged += _onLanguageChanged;
    }

    public string Name => "Photo Studio";

    public void Dispose()
    {
        _services.Framework.Update -= _onFrameworkUpdate;
        _loc.LanguageChanged -= _onLanguageChanged;
        _services.PhotoMode.CutsceneChanged -= _onCutsceneChanged;
        _photoModeAttach.Dispose();
        foreach (var h in _hotkeys) h.Dispose();
        _hotkeys.Clear();
        _hideAllToken?.Dispose();
        _hideAllToken = null;
        FlushWorkingLook();
        ReleaseLiveHides();
        RemoveWindows();
        _look.Dispose();
    }

    private void OnUpdate(float dt)
    {
        _look.Tick();
        TickStudio(dt);                          // Plugin.Studio.cs — toast timer, flash fade, tip reposition
    }

    private CaptureRequest BuildRequest()
    {
        var folder = EffectiveFolder(out _folderFellBack);
        // Camera-render capture never contains UI or nameplates, so only world layers need hiding for it — hiding
        // the HUD too would just flicker it for two frames on every capture.
        var worldLayers = _settings.Hides & (VisibilityLayers.OtherPlayers | VisibilityLayers.KeepParty);
        var s = new CaptureSettings(EffectiveScale(), _settings.Format, _settings.JpgQuality, folder, worldLayers);
        return CaptureController.BuildRequest(s, DateTime.Now, _screenshotFolder);
    }

    private void OnCaptureResult(CaptureResult r)
    {
        if (!r.Success)
        {
            _services.Notifications.Notify(_loc.TFormat("toast.failed", r.Error ?? ""), NotificationKind.Error);
            return;
        }
        WriteSidecar(r);
        _view.ShowSavedToast(r);
    }

    private void WriteSidecar(CaptureResult r)
    {
        if (r.Path is null) return; // Success is true only when CaptureResult.Ok wrote a path; defensive only.
        try
        {
            var json = CaptureController.SidecarJson(r, _activePresetName, MapName(), CapturedScale(r), _look.Draft);
            File.WriteAllText(Path.ChangeExtension(r.Path, ".json"), json);
        }
        catch (Exception ex)
        {
            _services.Log.Warning("[PhotoStudio] sidecar write failed: " + ex);
        }
    }

    private void TogglePanel()
    {
        // Both overlay hides (the Capture-tab toggle and hide-all) also hide this panel — the panel hotkey is the
        // way back, so it releases them first instead of toggling a panel nobody can see.
        if (_overlayHidden || _hideAllToken is not null)
        {
            _overlayHidden = false;
            _hideAllToken?.Dispose();
            _hideAllToken = null;
            ApplyLiveHides();
            _panel.Set(true);
            return;
        }
        _panel.Toggle();
    }

    private void ToggleHideAll()
    {
        // Anything hiding the overlay counts as "hidden": pressing hide-all again must bring everything back.
        if (_hideAllToken is not null || _overlayHidden)
        {
            _hideAllToken?.Dispose();
            _hideAllToken = null;
            _overlayHidden = false;
            ApplyLiveHides();
            return;
        }
        _hideAllToken = _services.SceneVisibility.Hide(VisibilityLayers.GameHud | VisibilityLayers.StellarOverlay | VisibilityLayers.Nameplates);
    }

    /// <summary>The scale the image was ACTUALLY captured at — the framework lowers 4× to 2× on the pixel cap or a
    /// memory fallback, so the requested setting would be wrong in the sidecar.</summary>
    private int EffectiveScale() => CaptureScale.Effective(_services.Framework.ScreenWidth, _services.Framework.ScreenHeight, _settings.Scale);

    private int CapturedScale(CaptureResult r)
    {
        var w = _services.Framework.ScreenWidth;
        return w > 0 ? Math.Max(1, (int)Math.Round((double)r.Width / w)) : _settings.Scale;
    }

    /// <summary>The map's display name for the sidecar; <c>CurrentSceneName</c> is a numeric scene id.</summary>
    private string MapName()
    {
        var id = _services.ClientState.CurrentSceneName;
        if (int.TryParse(id, out var sceneId) && _services.GameData.World.GetScene(sceneId) is { } scene && scene.Name.Length > 0)
            return scene.Name;
        return id ?? "";
    }

    private void NextPreset()
    {
        var all = _presets.All;
        if (all.Count == 0) return;
        var i = IndexOfPreset(_activePresetName);
        ApplyPreset(all[(i + 1) % all.Count]);
    }
}
