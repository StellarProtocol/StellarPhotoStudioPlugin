using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// The panel's composed status texts. Polled Funcs only hand back cached strings (Plugin-Performance-Rules §2): the
// footer line is keyed on its inputs and rebuilt only when one changes; texts that read the game (the orbit subject's
// name) or the hotkey bindings are refreshed by RefreshPanelTexts at 4 Hz while the panel is open, and each is rebuilt
// only when its inputs differ.
public sealed partial class Plugin
{
    private const float PanelTextsInterval = 0.25f;
    private float _panelTextsIn;

    private string _hdrStatus = string.Empty;   // header: "Free camera · Orbiting Nora · Frozen" / "Free camera off · Preset: X"
    private bool _hdrStatusAccent;              // accent while the free camera is on, muted while off (prototype)
    private string _fcStatus = string.Empty;    // Camera › Free camera status line
    private string _sceneStatus = string.Empty; // Camera › Scene status line
    private string _keysBody = string.Empty;    // Keys pill popover (the 1.x footer hotkey line)
    private string _fovNote = string.Empty;     // Camera › Lens note under the slider
    private string _fovHelp = string.Empty;     // Camera › Lens "?" body (names the FOV keys)
    private string _hideAllNote = string.Empty; // Hide › Hide now: "Ctrl+F10 hides everything at once."

    private Cached<(bool, FreeCamMode, string, bool, string, string)>? _hdrCache;
    private Cached<(bool, FreeCamMode, string, bool, string, string)>? _fcCache;
    private Cached<(bool, int, string)>? _sceneCache;
    private Cached<(string, string, string, string)>? _keysCache;
    private Cached<(bool, string, string, string, string, string)>? _fovNoteCache;
    private Cached<(string, string, string, string)>? _fovHelpCache;
    private Cached<(string, string)>? _hideAllCache;

    /// <summary>From TickStudio: only while the panel is open, at 4 Hz (and at once on the first tick after it opens).</summary>
    private void TickPanelTexts(float dt)
    {
        if (!_panelShown) { _panelTextsIn = 0f; return; }
        _panelTextsIn -= dt;
        if (_panelTextsIn > 0f) return;
        _panelTextsIn = PanelTextsInterval;
        RefreshPanelTexts();
    }

    private void RefreshPanelTexts()
    {
        bool on = _freeCam.Active, frozen = SceneFrozen, kept = SceneIsSet;
        var mode = _freeCam.Mode;
        var subject = on && mode == FreeCamMode.Orbit ? SubjectName() : string.Empty;
        string capKey = BindingText(StudioHotkeys.Capture), panelKey = BindingText(StudioHotkeys.Panel);
        string hideKey = BindingText(StudioHotkeys.HideAll), camKey = BindingText(StudioHotkeys.FreeCam);
        string fovIn = BindingText(StudioHotkeys.FovIn), fovOut = BindingText(StudioHotkeys.FovOut);
        string fovReset = BindingText(StudioHotkeys.FovReset);

        _hdrCache ??= new Cached<(bool, FreeCamMode, string, bool, string, string)>(k => HeaderStatus(k.Item1, k.Item2, k.Item3, k.Item4, k.Item5));
        _hdrStatus = _hdrCache.Get((on, mode, subject, frozen, _activePresetName, T("ps.hdr.camOff")));
        _hdrStatusAccent = on;

        _fcCache ??= new Cached<(bool, FreeCamMode, string, bool, string, string)>(k =>
            !k.Item1 ? _loc.TFormat(k.Item4 ? "fc.status.kept" : "fc.status.off", k.Item5)
            : k.Item2 == FreeCamMode.Fly ? T("fc.status.fly")
            : _loc.TFormat("fc.status.orbit", k.Item3));
        _fcStatus = _fcCache.Get((on, mode, subject, kept, camKey, T("fc.status.off")));

        _sceneCache ??= new Cached<(bool, int, string)>(k => SceneStatusText(k.Item1, k.Item2));
        _sceneStatus = _sceneCache.Get((frozen, ScenePosedCount, T("sc.status.empty")));

        _keysCache ??= new Cached<(string, string, string, string)>(k => _loc.TFormat("ps.hint.hotkeys", k.Item1, k.Item2, k.Item3));
        _keysBody = _keysCache.Get((capKey, panelKey, hideKey, T("ps.hint.hotkeys")));

        _fovNoteCache ??= new Cached<(bool, string, string, string, string, string)>(k => k.Item1
            ? _loc.TFormat("ps.look.fovKeys", k.Item3, k.Item2, k.Item4)
            : _loc.TFormat("ps.look.fovOff", k.Item5));
        _fovNote = _fovNoteCache.Get((on, fovIn, fovOut, fovReset, camKey, T("ps.look.fovKeys")));

        _fovHelpCache ??= new Cached<(string, string, string, string)>(k => _loc.TFormat("ps.help.look.fov", k.Item1, k.Item2, k.Item3));
        _fovHelp = _fovHelpCache.Get((fovIn, fovOut, fovReset, T("ps.help.look.fov")));

        _hideAllCache ??= new Cached<(string, string)>(k => _loc.TFormat("ps.hide.allNote", k.Item1));
        _hideAllNote = _hideAllCache.Get((hideKey, T("ps.hide.allNote")));
    }

    private string HeaderStatus(bool on, FreeCamMode mode, string subject, bool frozen, string preset)
    {
        var sb = new System.Text.StringBuilder(64);
        sb.Append(T(on ? "ps.hdr.camOn" : "ps.hdr.camOff"));
        if (on) sb.Append(" · ").Append(mode == FreeCamMode.Fly ? T("ps.hdr.flying") : _loc.TFormat("ps.hdr.orbiting", subject));
        if (frozen) sb.Append(" · ").Append(T("ps.hdr.frozen"));
        if (!on) sb.Append(" · ").Append(_loc.TFormat("ps.hdr.preset", preset));
        return sb.ToString();
    }

    private string SceneStatusText(bool frozen, int posed)
    {
        var n = posed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (frozen) return posed > 0 ? _loc.TFormat("sc.status.frozen", n) : T("sc.status.frozenOnly");
        return posed > 0 ? _loc.TFormat("sc.status.posed", n) : T("sc.status.empty");
    }

    // ── footer line ───────────────────────────────────────────────────────────────────────────────────────────────
    // Keyed on everything it shows, so the per-refresh poll is a tuple compare (PlannedSize is itself cached).
    private Cached<(bool, bool, int, int, CaptureFormat, PhotoShape, int, int, (int, string))>? _statusCache;

    private string StatusLine()
    {
        var size = PlannedSize();
        var mode = _captureGate.Armed && _rs.Pending ? 1 : Capturing ? 2 : 0;
        _statusCache ??= new Cached<(bool, bool, int, int, CaptureFormat, PhotoShape, int, int, (int, string))>(k => BuildStatusLine(k.Item9.Item1));
        return _statusCache.Get((_rs.Pending, Capturing, _settings.Scale, EffectiveScale(), _settings.Format, _settings.Shape,
            size.Width, size.Height, (mode, T("ps.status.capped"))));
    }

    private string BuildStatusLine(int mode)
    {
        if (mode == 1) return T("ps.rs.status.applying");   // R1: waiting for a ReShade switch
        if (mode == 2) return _settings.Scale == 4 ? T("ps.status.capturing4x") : T("ps.capturing");
        var scale = EffectiveScale();
        if (_settings.Shape != PhotoShape.Screen)   // a shape keeps its scale and shrinks both sides; the size is real
            return ShapeFrame.StatusText(_settings.Scale, FormatName(), PlannedSize(), _settings.Shape);
        return scale < _settings.Scale
            ? _loc.TFormat("ps.status.capped", _settings.Scale, scale, FormatName(), ShapeFrame.SizeText(PlannedSize()))
            : $"{scale}× · {FormatName()} · {ShapeFrame.SizeText(PlannedSize())}";
    }

    private Cached<(string, string)>? _rsOnCache;

    private string ReShadeOnSuffix()
    {
        _rsOnCache ??= new Cached<(string, string)>(k => " · " + k.Item1);
        var on = T("ps.rs.status.on");
        return _rsOnCache.Get((on, on));
    }

    private string FormatName() => _settings.Format == CaptureFormat.Jpg ? "JPG" : "PNG";

    // The REAL output size (framework plan: scale caps, shape and GPU limit) — spec 2026-10-03 § 4. Cached on the size.
    private Cached<(int, int)>? _resCache;

    private string ResolutionText()
    {
        var size = PlannedSize();
        _resCache ??= new Cached<(int, int)>(k => ShapeFrame.SizeText(new CaptureSize(k.Item1, k.Item2)));
        return _resCache.Get((size.Width, size.Height));
    }

    // ── hotkey labels ─────────────────────────────────────────────────────────────────────────────────────────────

    private string BindingText(string id)
    {
        foreach (var h in _hotkeys)
            if (h.Id == id) return h.CurrentBinding is { } b ? KeyLabel(b) : "—";
        return "—";
    }

    /// <summary>"Shift+F10", and the bracket keys as the characters on the key ("[", "]", "\") rather than their names.</summary>
    internal static string KeyLabel(KeyBinding b) =>
        b.ToString().Replace("LeftBracket", "[").Replace("RightBracket", "]").Replace("Backslash", "\\");
}
