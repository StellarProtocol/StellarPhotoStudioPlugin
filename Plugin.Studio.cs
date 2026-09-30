using System;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Presets;

namespace Stellar.PhotoStudio;

// Panel coordination: window registration, live hides while composing, preset selection / modified tracking,
// the saved toast timer and the shutter flash. Tab contents live in Plugin.Panel.*.cs; the docked strip in
// Plugin.Docked.cs; the "?" popover in Plugin.Help.cs.
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

    private string _activePresetName = "Natural";
    private bool _modified;
    private LookSettings? _unsavedLook;  // edits kept when another preset was applied over them (panel session only)

    private const float ToastSeconds = 6f;
    private float _toastLeft;
    private float _flash;

    private bool InWorld() => _services.ClientState.Phase == GamePhase.World
                              && (_services.ClientState.UiState & GameUIState.Loading) == 0;

    private void RegisterWindows()
    {
        _panelWin = RegisterPanelWindow();      // Plugin.Panel.cs
        _dockedWin = RegisterDockedWindow();    // Plugin.Docked.cs
        _toastWin = RegisterToastWindow();      // Plugin.Toast.cs
        _tipWindow = RegisterTipWindow();       // Plugin.Help.cs
        _flashWin = RegisterFlashWindow();      // Plugin.Toast.cs
    }

    private void RemoveWindows()
    {
        foreach (var w in new[] { _panelWin, _dockedWin, _toastWin, _tipWindow, _flashWin })
            w?.Remove();
    }

    // ── panel / docked open state ────────────────────────────────────────────────────────────────────────────

    private void ShowPanel(bool show)
    {
        if (_panelShown == show) return;
        _panelShown = show;
        _panelWin.SetVisible(show);
        if (show) RescanLuts();
        if (show && _dockedShown) ShowDocked(false);    // never both sets of controls at once
        if (!show && _tipWindow.IsShown) { _tipKey = ""; _tipWindow.SetVisible(false); }
        ApplyLiveHides();
    }

    private void ShowDocked(bool show)
    {
        if (_dockedShown == show) return;
        _dockedShown = show;
        _dockedWin.SetVisible(show);
        ApplyLiveHides();
    }

    /// <summary>Called by <see cref="PhotoModeAttach"/> when the game's own photo mode starts / ends.</summary>
    private void SetDockedForGamePhotoMode(bool active)
    {
        if (!active) { _dockedDismissed = false; ShowDocked(false); return; }
        if (_settings.DockedAuto && !_panelShown && !_dockedDismissed) ShowDocked(true);
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
        ReleaseLiveHides();
        if (!_panelShown && !_dockedShown && !_overlayHidden) return;
        var layers = _settings.Hides | (_overlayHidden ? VisibilityLayers.StellarOverlay : VisibilityLayers.None);
        if ((layers & VisibilityLayers.OtherPlayers) == 0) layers &= ~VisibilityLayers.KeepParty;
        if (layers != VisibilityLayers.None) _liveHideToken = _services.SceneVisibility.Hide(layers);
    }

    private void ReleaseLiveHides()
    {
        _liveHideToken?.Dispose();
        _liveHideToken = null;
    }

    private bool IsHidden(VisibilityLayers layer) => layer == VisibilityLayers.StellarOverlay
        ? _overlayHidden
        : (_settings.Hides & layer) != 0;

    private void SetHidden(VisibilityLayers layer, bool hidden)
    {
        if (layer == VisibilityLayers.StellarOverlay) _overlayHidden = hidden;
        else _settings.SetHides(hidden ? _settings.Hides | layer : _settings.Hides & ~layer);
        ApplyLiveHides();
    }

    private bool LayerAvailable(VisibilityLayers layer) => (_services.SceneVisibility.Available & layer) == layer;

    // ── presets + working look ───────────────────────────────────────────────────────────────────────────────

    private void RestoreWorkingLook()
    {
        _activePresetName = _settings.PresetName;
        var preset = FindPreset(_activePresetName) ?? _presets.All[0];
        _activePresetName = preset.Name;
        var working = ParseLook(_settings.WorkingJson);
        _editor.Load(working ?? preset.Look);
        _modified = working is not null;
        _look.SetDraft(_editor.Build());
    }

    private void OnEditorChanged()
    {
        var built = _editor.Build();
        _look.SetDraft(built);
        _modified = true;
        _settings.SetWorkingJson(System.Text.Json.JsonSerializer.Serialize(PresetDto.From(_activePresetName, built)));
    }

    private void ApplyPreset(Preset p)
    {
        if (_modified) _unsavedLook = _editor.Build();
        _activePresetName = p.Name;
        _settings.SetPresetName(p.Name);
        _editor.Changed -= OnEditorChanged;
        _editor.Load(p.Look);
        _editor.Changed += OnEditorChanged;
        _modified = false;
        _settings.SetWorkingJson(null);
        _look.SetDraft(_editor.Build());
    }

    private void RestoreUnsavedLook()
    {
        if (_unsavedLook is null) return;
        var look = _unsavedLook;
        _unsavedLook = null;
        _editor.Load(look);   // raises Changed → marks modified + persists the working look
    }

    private Preset? FindPreset(string name)
    {
        foreach (var p in _presets.All)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    private int IndexOfPreset(string name)
    {
        var all = _presets.All;
        for (var i = 0; i < all.Count; i++)
            if (string.Equals(all[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    private bool ActiveIsBuiltIn => FindPreset(_activePresetName)?.BuiltIn ?? true;

    private static LookSettings? ParseLook(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<PresetDto>(json)?.ToLook(); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    // ── capture ──────────────────────────────────────────────────────────────────────────────────────────────

    private void CaptureNow()
    {
        _flash = 0.6f;
        _flashWin.SetVisible(true);
        _ = _session.CaptureAsync();
    }

    private bool Capturing => _session.State == StudioState.Capturing;

    /// <summary>The custom folder when it exists or can be created, else the default (flagged for the toast).</summary>
    private string EffectiveFolder(out bool fellBack)
    {
        fellBack = false;
        if (string.IsNullOrWhiteSpace(_settings.Folder)) return _screenshotFolder;
        try
        {
            Directory.CreateDirectory(_settings.Folder);
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
