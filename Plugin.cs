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
    private readonly PresetStore _presets;
    private readonly StudioSession _session;
    private readonly IStudioView _view;
    private readonly PanelOpenState _panel;
    private readonly PhotoModeAttach _photoModeAttach;
    private readonly Action<float> _onFrameworkUpdate;
    private readonly Action<bool> _onCutsceneChanged;

    private int _presetIndex;
    private IDisposable? _hideAllToken;
    private readonly string _screenshotFolder;

    // No panel yet to drive this (Task 14): a fixed, capture-correctness-only default so a screenshot never
    // includes the plugin's own overlay chrome. Everything else (scale/format/quality/folder/extra hides) is
    // left at the framework's own CaptureRequest defaults until the panel can set them.
    private readonly CaptureSettings _captureSettings = new(
        Scale: 2, Format: CaptureFormat.Png, JpgQuality: 92, Folder: null, Hide: VisibilityLayers.StellarOverlay);

    public Plugin(IPluginServices services)
    {
        _services = services;
        _loc = services.Localization;
        LogBootDiag(); // Plugin.Diagnostics.cs — gated on StellarDiagnostics.IsEnabled

        var assemblyDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
        var root = GameRootLocator.Resolve(AppContext.BaseDirectory, assemblyDir, Directory.Exists);
        _screenshotFolder = Path.Combine(root.Path, "stellar", "screenshots");
        // Not diagnostic spam — a plain, always-on boot line so the resolved path is visible in a normal log.
        services.Log.Info($"[PhotoStudio] game root resolved: {root.Path} (verified={root.Verified})");

        _view = new NoOpStudioView();

        _look = new LookController(services.RenderLook);
        _presets = new PresetStore(new DataStorePresetFiles(services.Data), m => services.Log.Warning(m));
        _look.SetDraft(_presets.All[_presetIndex].Look);

        _session = new StudioSession(services.ScreenCapture, BuildRequest, OnCaptureResult, services.Log.Warning);
        _panel = new PanelOpenState(_view, _look);

        DeclareHotkeys();

        _photoModeAttach = new PhotoModeAttach(services.PhotoMode, _look, _panel);
        _onCutsceneChanged = suspended => _look.SetSuspended(suspended);
        services.PhotoMode.CutsceneChanged += _onCutsceneChanged;

        _onFrameworkUpdate = _ => _look.Tick();
        services.Framework.Update += _onFrameworkUpdate;
    }

    public string Name => "Photo Studio";

    public void Dispose()
    {
        _services.Framework.Update -= _onFrameworkUpdate;
        _services.PhotoMode.CutsceneChanged -= _onCutsceneChanged;
        _photoModeAttach.Dispose();
        foreach (var h in _hotkeys) h.Dispose();
        _hotkeys.Clear();
        _hideAllToken?.Dispose();
        _hideAllToken = null;
        _look.Dispose();
    }

    private CaptureRequest BuildRequest() => CaptureController.BuildRequest(_captureSettings, DateTime.Now, _screenshotFolder);

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
            var preset = _presets.All[_presetIndex];
            var json = CaptureController.SidecarJson(r, preset.Name, _services.ClientState.CurrentSceneName ?? "", _look.Draft);
            File.WriteAllText(Path.ChangeExtension(r.Path, ".json"), json);
        }
        catch (Exception ex)
        {
            _services.Log.Warning("[PhotoStudio] sidecar write failed: " + ex);
        }
    }

    private void TogglePanel() => _panel.Toggle();

    private void ToggleHideAll()
    {
        if (_hideAllToken is not null)
        {
            _hideAllToken.Dispose();
            _hideAllToken = null;
            return;
        }
        _hideAllToken = _services.SceneVisibility.Hide(VisibilityLayers.GameHud | VisibilityLayers.StellarOverlay | VisibilityLayers.Nameplates);
    }

    private void NextPreset()
    {
        if (_presets.All.Count == 0) return;
        _presetIndex = (_presetIndex + 1) % _presets.All.Count;
        _look.SetDraft(_presets.All[_presetIndex].Look);
    }
}
