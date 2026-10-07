using System;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Presets;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

// Panel coordination: window registration, live hides while composing, the preset session, the saved toast timer
// and the shutter flash. Tab contents live in Plugin.Panel.*.cs; the docked strip in Plugin.Docked.cs; the "?"
// popover in Plugin.Help.cs.
public sealed partial class Plugin
{
    private IWindowControl _panelWin = null!;
    private IWindowControl _dockedWin = null!;
    private IWindowControl _toastWin = null!;
    private IWindowControl _flashWin = null!;

    private bool _panelShown;
    private bool _dockedShown;
    private bool _dockedDismissed;       // ✕ on the strip hides it for the current game-photo-mode visit only
    private bool _overlayHidden;         // never persisted (see StudioSettings)
    private bool _folderFellBack;
    private IDisposable? _liveHideToken;

    private PresetSession _presetSession = null!;

    // The working look is saved debounced: a slider drag fires every frame, and every config save is a
    // main-thread file write (perf review blocker).
    private const float WorkingSaveDelay = 0.5f;
    private float _workingSaveIn = -1f;

    private const float ToastSeconds = 6f;
    private float _toastLeft;
    private float _flash;

    // Layers the framework can drive, read once per framework tick — the panel's lambdas poll it many times.
    private VisibilityLayers _availableThisTick = (VisibilityLayers)0x1F;

    private bool InWorld() => _services.ClientState.Phase == GamePhase.World
                              && (_services.ClientState.UiState & GameUIState.Loading) == 0;

    private void RegisterWindows()
    {
        _panelWin = RegisterPanelWindow();      // Plugin.Panel.cs
        _dockedWin = RegisterDockedWindow();    // Plugin.Docked.cs
        _toastWin = RegisterToastWindow();      // Plugin.Toast.cs
        _tipWindow = RegisterTipWindow();       // Plugin.Help.cs
        _flashWin = RegisterFlashWindow();      // Plugin.Toast.cs
        _freeCamHudWin = RegisterFreeCamHud();   // Plugin.FreeCamHud.cs
    }

    private void RemoveWindows()
    {
        foreach (var w in new[] { _panelWin, _dockedWin, _toastWin, _tipWindow, _flashWin, _freeCamHudWin })
            w?.Remove();
        HideGuide();   // Plugin.FrameGuide.cs
        RemoveMarkers();   // Plugin.LampMarkers.cs
    }

    // ── panel / docked open state ────────────────────────────────────────────────────────────────────────────

    private void ShowPanel(bool show)
    {
        if (_panelShown == show) return;
        _panelShown = show;
        _panelWin.SetVisible(show);
        _quality.SetComposing(_panelShown || _dockedShown);
        if (show) RescanLuts();
        if (show && _dockedShown) ShowDocked(false);    // never both sets of controls at once
        if (!show && _tipWindow.IsShown) { _tipKey = ""; _tipWindow.SetVisible(false); }
        if (!show) FlushWorkingLook();
        ApplyLiveHides();
        // Closing the full panel while the game's photo mode is still open brings the strip back.
        if (!show) SetDockedForGamePhotoMode(_services.PhotoMode.IsActive);
        SyncStudioOpen();   // Plugin.ReShade.cs — after the strip decision, so panel → strip is not a close
    }

    private void ShowDocked(bool show)
    {
        if (_dockedShown == show) return;
        _dockedShown = show;
        _dockedWin.SetVisible(show);
        _look.SetGamePhotoActive(show);
        _quality.SetComposing(_panelShown || _dockedShown);   // preview the look in the game's photo mode only with our controls on screen
        if (show && _toastWin.IsShown) _toastWin.SetVisible(false);   // the strip shows the saved line itself
        ApplyLiveHides();
        SyncStudioOpen();
    }

    /// <summary>Called by <see cref="PhotoModeAttach"/> when the game's own photo mode starts / ends.</summary>
    private void SetDockedForGamePhotoMode(bool active)
    {
        if (!active) _dockedDismissed = false;
        ShowDocked(active && _settings.DockedAuto && !_panelShown && !_dockedDismissed);
        ApplyLiveHides();   // the Game-HUD hide is masked inside the game's photo mode (see ApplyLiveHides)
    }

    private void DismissDocked()
    {
        _dockedDismissed = true;
        ShowDocked(false);
    }

    private void DockedToFullPanel()
    {
        ShowDocked(false);
        _panel.Set(true);
    }

    // ── live hides (applied while the panel or the docked strip is open) ─────────────────────────────────────

    private void ApplyLiveHides()
    {
        // Take the new token BEFORE releasing the old one: with the old one gone first, the framework briefly sees
        // "nothing hidden", shows everything and hides it again — a reflection round-trip and a visible flicker.
        var previous = _liveHideToken;
        _liveHideToken = null;
        if (_panelShown || _dockedShown || _overlayHidden)
        {
            var layers = _settings.Hides | (_overlayHidden ? VisibilityLayers.StellarOverlay : VisibilityLayers.None);
            layers = HideLayers.ToRequest(layers);   // all four player groups = the game's "no other player" switch
            // The game's own photo controls live under the same UI root as its HUD: hiding "Game HUD" there would
            // take them away too (the game's [F] key hides its own interface in photo mode).
            if (_services.PhotoMode.IsActive) layers &= ~VisibilityLayers.GameHud;
            if (layers != VisibilityLayers.None) _liveHideToken = _services.SceneVisibility.Hide(layers);
        }
        previous?.Dispose();
    }

    private void ReleaseLiveHides()
    {
        _liveHideToken?.Dispose();
        _liveHideToken = null;
    }

    private bool IsHidden(VisibilityLayers layer) => layer == VisibilityLayers.StellarOverlay
        ? _overlayHidden
        : (_settings.Hides & layer) == layer;   // a set (the docked strip's player groups) counts only when all are on

    private void SetHidden(VisibilityLayers layer, bool hidden)
    {
        if (layer == VisibilityLayers.StellarOverlay) _overlayHidden = hidden;
        else _settings.SetHides(hidden ? _settings.Hides | layer : _settings.Hides & ~layer);
        ApplyLiveHides();
    }

    private bool LayerAvailable(VisibilityLayers layer) => (_availableThisTick & layer) == layer;

    // ── presets + working look ───────────────────────────────────────────────────────────────────────────────

    private string _activePresetName => _presetSession.ActiveName;
    private bool _modified => _presetSession.Modified;
    private bool ActiveIsBuiltIn => _presetSession.ActiveIsBuiltIn;
    private Preset? FindPreset(string name) => _presetSession.Find(name);
    // The lights' result reaches the lamp toast (Unavailable = the preset's lamps could not be placed — lights review).
    private void ApplyPreset(Preset p) => LampToast(_presetSession.Apply(p));

    private void StartPresetSession()
    {
        _presetSession = new PresetSession(_presets, _editor, _settings.PresetName, ParseLook(_settings.WorkingJson),
            new PresetShapeLink(() => _settings.Shape, _settings.SetShape));
        _presetSession.StateChanged += OnPresetStateChanged;
        _presetSession.ReShadeLink = new PresetReShadeLink(_rs.Current, _rs.Apply);   // R8: looks remember ReShade
        _settings.ShapeChanged += _presetSession.OnShapeChanged;   // a shape change marks the preset modified
        _look.SetDraft(DraftFromEditor());
    }

    private void OnEditorChanged()
    {
        _look.SetDraft(DraftFromEditor());
        _workingSaveIn = WorkingSaveDelay;
    }

    private void OnPresetStateChanged()
    {
        _settings.SetPresetName(_presetSession.ActiveName);
        if (_presetSession.Modified) _workingSaveIn = WorkingSaveDelay;
        else { _workingSaveIn = -1f; _settings.SetWorkingJson(null); }
        _presetNamesCache = null;
    }

    private void FlushWorkingLook()
    {
        if (_workingSaveIn < 0f) return;
        _workingSaveIn = -1f;
        _settings.SetWorkingJson(_presetSession.Modified
            ? System.Text.Json.JsonSerializer.Serialize(PresetDto.From(_presetSession.ActiveName, _editor.Build()))
            : null);
    }

    private int IndexOfPreset(string name)
    {
        var all = _presets.All;
        for (var i = 0; i < all.Count; i++)
            if (string.Equals(all[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    private static LookSettings? ParseLook(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<PresetDto>(json)?.ToLook(); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>The editor keeps LUTs as a file name (so presets travel between PCs); the framework needs the path.</summary>
    private LookSettings DraftFromEditor()
    {
        var built = _editor.Build();
        if (built.Lut is not { } lut || Path.IsPathRooted(lut.FilePath)) return built;
        return built with { Lut = lut with { FilePath = Path.Combine(LutFolder, lut.FilePath) } };
    }

    // ── capture ──────────────────────────────────────────────────────────────────────────────────────────────

    // A capture-only shadow boost reallocates the shadow map; give it a couple of frames before the grab. A ReShade
    // switch (Look preset, preset, on/off, effect) must be applied first too — R1 / D8 (CaptureGate, Plugin.ReShade.cs).
    private const int BoostSettleTicks = 2;

    private void CaptureNow()
    {
        if (Capturing || _captureGate.Armed) return;
        _quality.SetCapturing(true);
        if (_quality.BoostingForCapture || _rs.Pending)
        {
            _captureGate.Arm(_quality.BoostingForCapture ? BoostSettleTicks : 0);   // the flash shows when it fires
            return;
        }
        StartFlash();
        _ = _session.CaptureAsync();
    }

    private void StartFlash()
    {
        _flash = 0.6f;
        _flashWin.SetRect(new WindowRect(0f, 0f, _services.Framework.ScreenWidth, _services.Framework.ScreenHeight));
        _flashWin.SetVisible(true);
    }

    private bool Capturing => _session.State == StudioState.Capturing;

    /// <summary>The custom folder when it exists (or can be created) and is writable, else the default
    /// (flagged for the toast). Probed with a real write — an existing read-only folder must fall back too.</summary>
    private string EffectiveFolder(out bool fellBack)
    {
        fellBack = false;
        if (string.IsNullOrWhiteSpace(_settings.Folder)) return _screenshotFolder;
        try
        {
            Directory.CreateDirectory(_settings.Folder);
            var probe = Path.Combine(_settings.Folder, ".photostudio-write-test");
            File.WriteAllBytes(probe, Array.Empty<byte>());
            try { File.Delete(probe); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // writable is what matters
            return _settings.Folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            fellBack = true;
            return _screenshotFolder;
        }
    }

    private void TickStudio(float dt)
    {
        _availableThisTick = _services.SceneVisibility.Available;
        if (_toastLeft > 0f)
        {
            _toastLeft -= dt;
            if (_toastLeft <= 0f) _toastWin.SetVisible(false);
        }
        if (_flash > 0f)
        {
            _flash = Math.Max(0f, _flash - dt * 4f);
            if (_flash <= 0f) _flashWin.SetVisible(false);
        }
        if (_workingSaveIn > 0f)
        {
            _workingSaveIn -= dt;
            if (_workingSaveIn <= 0f) { _workingSaveIn = 0f; FlushWorkingLook(); }
        }
        TickHourSave(dt);
        TipRepositionTick();
    }

    /// <summary>The real panel behind the <see cref="IStudioView"/> seam.</summary>
    private sealed class WindowStudioView : IStudioView
    {
        private readonly Plugin _p;
        public WindowStudioView(Plugin p) => _p = p;
        public bool IsOpen => _p._panelShown;
        public void Open() => _p.ShowPanel(true);
        public void Close() => _p.ShowPanel(false);
        public void ShowSavedToast(CaptureResult result) => _p.ShowToast(result);
        public void ShowError(string message) => _p._services.Notifications.Notify(message, NotificationKind.Warning);
    }
}
