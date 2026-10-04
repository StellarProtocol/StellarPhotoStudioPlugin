using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>
/// Photo Studio's single door to <see cref="IReShade"/>. Every request (1) is sent, (2) is remembered as a "wish" so the
/// panel shows the new state at once (the service shows it only after a later Changed), and (3) arms the
/// <see cref="ReShadeSettle"/> capture gate (R1). Wishes are dropped once everything is applied or the gate times out.
/// <see cref="Enabled"/> reads a value refreshed on <see cref="OnChanged"/> — IReShade.Enabled costs five add-on calls
/// per read and the panel polls it every refresh; the settle check reads the same value. Owner ruling O1 (2026-10-04):
/// <see cref="OnStudioOpened"/> snapshots ReShade's on/off + preset and <see cref="OnStudioClosed"/> puts them back, so
/// closing Photo Studio returns the game's look. Main thread only.
/// </summary>
internal sealed class ReShadeControl
{
    private readonly IReShade _rs;
    private readonly ReShadeSettle _settle;
    private readonly string _presetFolder;
    private readonly Dictionary<(string Effect, string Name), bool> _wantTech = new();
    private string? _wantPreset;
    private bool? _wantEnabled;
    private bool _liveEnabled;
    private string? _livePreset;
    private ReShadeChoice? _openSnapshot;   // O1: ReShade as it was when Photo Studio opened (full preset path)
    private bool _open = true;                // tests drive the setters directly; the plugin calls OnStudioClosed at start
    private ReShadeChoice? _session;          // the studio's ReShade state, re-applied at the next open (edits never lost)

    public ReShadeControl(IReShade rs, string presetFolder)
    {
        _rs = rs;
        _settle = new ReShadeSettle(rs);
        _presetFolder = presetFolder;
        IsOnFunc = IsOn;
        OnChanged();
    }

    public bool Installed => _rs.State != ReShadeState.NotInstalled;
    public bool Pending => _settle.Pending;
    public bool TimedOut => _settle.TimedOut;
    public bool Enabled => _wantEnabled ?? _liveEnabled;
    public string? PresetPath => _wantPreset ?? _rs.CurrentPreset;
    public string? PresetFile => PresetPath is { } p ? ReShadePaths.FileName(p) : null;

    public bool IsOn(ReShadeTechnique t) => _wantTech.TryGetValue((t.EffectFile, t.Name), out var on) ? on : t.Enabled;

    /// <summary><see cref="IsOn"/> as ONE delegate created at construction — pass this (not a method group, which
    /// allocates a new delegate per call) to per-refresh code such as <see cref="ReShadeView.Depth"/>.</summary>
    public Func<ReShadeTechnique, bool> IsOnFunc { get; }

    /// <summary>Call from IReShade.Changed. True when ReShade's current preset changed since the last call (on/off or a
    /// technique alone does not count) — the panel rebuilds its preset list only then.</summary>
    public bool OnChanged()
    {
        _liveEnabled = Installed && _rs.Enabled;
        // O1: a close that hit a reload could not restore — finish it the moment ReShade is Ready again (qa re-review 2).
        if (!_open && _openSnapshot is not null && _rs.State == ReShadeState.Ready) OnStudioClosed();
        var preset = _rs.CurrentPreset;
        if (string.Equals(preset, _livePreset, StringComparison.Ordinal)) return false;
        _livePreset = preset;
        return true;
    }

    /// <summary>Call once per framework tick, before the capture gate.</summary>
    public void Tick(float dt)
    {
        _settle.Tick(dt);
        if (_settle.Pending) return;
        // All-or-nothing by design: the wishes are cleared together, only when the whole settle releases (every switch
        // applied, or the 3 s cap hit). Until then the panel keeps showing every outstanding wish.
        _wantPreset = null;
        _wantEnabled = null;
        _wantTech.Clear();
    }

    public void SetEnabled(bool on)
    {
        if (!Installed) return;
        _wantEnabled = on;
        _rs.Enabled = on;
        _settle.Expect(() => _liveEnabled == on);   // refreshed on Changed: no add-on read per tick
    }

    public void SetPresetPath(string path)
    {
        if (!Installed || string.IsNullOrEmpty(path)) return;
        _wantPreset = path;
        _rs.SetPreset(path);
        var want = ReShadePaths.Norm(path);   // once, at the request — not per settle check
        _settle.Expect(() => _rs.CurrentPreset is { } now && ReShadePaths.SameNorm(ReShadePaths.Norm(now), want));
    }

    public void SetTechnique(ReShadeTechnique t, bool on)
    {
        if (!Installed) return;
        _wantTech[(t.EffectFile, t.Name)] = on;
        _rs.SetTechnique(t.EffectFile, t.Name, on);
        _settle.Expect(() => TechniqueIs(t.EffectFile, t.Name, on));
    }

    /// <summary>What a Look preset saves; null when ReShade is not installed (nothing to remember). The preset is the FULL
    /// path (ReShade's own preset outside our folder included); <see cref="Presets.ReShadeDto"/> decides what reaches disk.</summary>
    public ReShadeChoice? Current() => Installed ? new ReShadeChoice(PresetPath, Enabled) : null;

    /// <summary>Applies a Look preset's choice: a bare file name is a preset in Photo Studio's presets folder, a full path
    /// (an in-memory baseline / stash row) is switched to as it is; then on/off.</summary>
    public void Apply(ReShadeChoice c)
    {
        if (!_open)   // closed (next-preset hotkey): wait for the open, after its snapshot (O1)
        {
            _session = c.Preset is null && _session is { } s ? s with { Enabled = c.Enabled } : c;   // on/off-only keeps the preset
            return;
        }
        if (c.Preset is { Length: > 0 } p)
            SetPresetPath(ReShadePaths.HasFolder(p) ? p : ReShadePaths.PathFor(_presetFolder, p));
        SetEnabled(c.Enabled);
    }

    /// <summary>O1: Photo Studio opened. Remembers ReShade's on/off and preset — unless a snapshot is still held from a
    /// close that could not restore (ReShade was not Ready then): that one is still what the game had.</summary>
    public void OnStudioOpened()
    {
        _open = true;
        if (_openSnapshot is null && Installed)
            _openSnapshot = new ReShadeChoice(PresetPath, Enabled);   // with wishes: a restore sent at a quick re-close counts
        if (_session is not { } s) return;
        _session = null;
        if (Installed) ApplyDiff(s);   // the state the studio had at close (or a look applied while closed) comes back
    }

    /// <summary>O1: Photo Studio closed (or the plugin unloads). Puts back only what differs from the snapshot; while
    /// ReShade is not Ready it sends nothing and keeps the snapshot for the next close.</summary>
    public void OnStudioClosed()
    {
        _open = false;
        if (_openSnapshot is not { } snap) return;
        if (!Installed) { _openSnapshot = null; return; }
        if (_rs.State != ReShadeState.Ready) return;
        _openSnapshot = null;
        var now = new ReShadeChoice(PresetPath, Enabled);
        _session = Differs(now, snap) ? now : null;   // remembered so a reopen brings the edit back
        ApplyDiff(snap);
    }

    private static bool Differs(ReShadeChoice a, ReShadeChoice b) =>
        a.Enabled != b.Enabled || (a.Preset is { Length: > 0 } p && !ReShadePaths.Same(p, b.Preset ?? ""));

    /// <summary>Sends only the parts of <paramref name="c"/> that differ from ReShade's current state.</summary>
    private void ApplyDiff(ReShadeChoice c)
    {
        if (c.Preset is { Length: > 0 } p)
        {
            var path = ReShadePaths.HasFolder(p) ? p : ReShadePaths.PathFor(_presetFolder, p);   // a look's bare file name
            if (!ReShadePaths.Same(PresetPath, path)) SetPresetPath(path);
        }
        if (Enabled != c.Enabled) SetEnabled(c.Enabled);
    }

    private bool TechniqueIs(string effect, string name, bool on)
    {
        var list = _rs.Techniques;
        for (var i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (t.Name == name && string.Equals(t.EffectFile, effect, StringComparison.OrdinalIgnoreCase)) return t.Enabled == on;
        }
        return false;
    }
}
