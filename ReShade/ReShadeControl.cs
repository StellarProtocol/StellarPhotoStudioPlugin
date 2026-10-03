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
/// per read and the panel polls it every refresh. Main thread only.
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

    public ReShadeControl(IReShade rs, string presetFolder)
    {
        _rs = rs;
        _settle = new ReShadeSettle(rs);
        _presetFolder = presetFolder;
        OnChanged();
    }

    public bool Installed => _rs.State != ReShadeState.NotInstalled;
    public bool Pending => _settle.Pending;
    public bool TimedOut => _settle.TimedOut;
    public bool Enabled => _wantEnabled ?? _liveEnabled;
    public string? PresetPath => _wantPreset ?? _rs.CurrentPreset;
    public string? PresetFile => PresetPath is { } p ? ReShadePaths.FileName(p) : null;

    public bool IsOn(ReShadeTechnique t) => _wantTech.TryGetValue((t.EffectFile, t.Name), out var on) ? on : t.Enabled;

    /// <summary>Call from IReShade.Changed.</summary>
    public void OnChanged() => _liveEnabled = Installed && _rs.Enabled;

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
        _settle.Expect(() => _rs.Enabled == on);
    }

    public void SetPresetPath(string path)
    {
        if (!Installed || string.IsNullOrEmpty(path)) return;
        _wantPreset = path;
        _rs.SetPreset(path);
        _settle.Expect(() => ReShadePaths.Same(_rs.CurrentPreset, path));
    }

    public void SetTechnique(ReShadeTechnique t, bool on)
    {
        if (!Installed) return;
        _wantTech[(t.EffectFile, t.Name)] = on;
        _rs.SetTechnique(t.EffectFile, t.Name, on);
        _settle.Expect(() => TechniqueIs(t.EffectFile, t.Name, on));
    }

    /// <summary>What a Look preset saves; null when ReShade is not installed (nothing to remember).</summary>
    public ReShadeChoice? Current() => Installed ? new ReShadeChoice(PresetFile, Enabled) : null;

    /// <summary>Applies a Look preset's choice: its preset file from Photo Studio's presets folder, then on/off.</summary>
    public void Apply(ReShadeChoice c)
    {
        if (c.Preset is { Length: > 0 } file) SetPresetPath(ReShadePaths.PathFor(_presetFolder, file));
        SetEnabled(c.Enabled);
    }

    private bool TechniqueIs(string effect, string name, bool on)
    {
        foreach (var t in _rs.Techniques)
            if (t.Name == name && string.Equals(t.EffectFile, effect, StringComparison.OrdinalIgnoreCase)) return t.Enabled == on;
        return false;
    }
}
