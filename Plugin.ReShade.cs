using System;
using System.Collections.Generic;
using System.IO;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.ReShade;

namespace Stellar.PhotoStudio;

// ReShade wiring (spec 2026-10-03 reshade § 6 + § 11; plan 2026-10-04). Logic lives in ReShade/*.cs (unit-tested); this
// partial connects it to IPluginServices: search paths at start (R5), IReShade.Changed, the per-tick settle gate (R1),
// the Look-preset link (R8), the shutter snapshot for the sidecar (R8) and the dxgi.dll presence for the panel (R3).
public sealed partial class Plugin
{
    private ReShadeControl _rs = null!;
    private PackInstaller _packs = null!;
    private readonly CaptureGate _captureGate = new();
    private Action _onReShadeChanged = null!;
    private string _gameRoot = "";
    private bool _dxgiPresent;
    private string? _reShadeVersion;
    private bool _reShadeVersionRead;
    private IReadOnlyList<string> _rsPresetFiles = Array.Empty<string>();
    private PresetOptions? _rsOptionsCache;    // the Preset dropdown (Plugin.Panel.ReShade.cs); dropped on Changed / rescan / pick
    private ShotReShade _shot;
    private bool _timeoutLogged;
    private bool _studioOpen;                  // the panel or the docked strip is on screen (SyncStudioOpen)

    /// <summary>ReShade's state at the shutter (BuildRequest), for the sidecar.</summary>
    private readonly record struct ShotReShade(bool Installed, bool On, string? Preset);

    private string ReShadePresetFolder => Path.Combine(_services.Downloads.DataFolder, "reshade", "presets");

    private void StartReShade(string gameRoot)
    {
        _gameRoot = gameRoot;
        _rs = new ReShadeControl(_services.ReShade, ReShadePresetFolder);
        _rs.OnStudioClosed();   // the plugin starts with Photo Studio closed: a hotkey look waits for the open (O1)
        _packs = new PackInstaller(_services.Downloads, PackCatalog.All, Directory.Exists, _services.Log.Warning);
        _packs.PacksChanged += ApplySearchPaths;
        _onReShadeChanged = OnReShadeChanged;
        _services.ReShade.Changed += _onReShadeChanged;
        _dxgiPresent = File.Exists(Path.Combine(_gameRoot, "dxgi.dll"));
        ReadReShadeVersionOnce();   // a plugin (re)load after ReShade is already Ready gets no Ready transition
        // R5: once at start, whatever the state — the framework holds it until the add-on binds and drops an identical
        // repeat (an empty list sends nothing). Again only when a pack is installed (PacksChanged).
        ApplySearchPaths();
        RescanReShadePresets();
    }

    private void StopReShade()
    {
        _rs.OnStudioClosed();   // O1: unloading returns ReShade to what it was when Photo Studio opened
        _services.ReShade.Changed -= _onReShadeChanged;
        _packs.PacksChanged -= ApplySearchPaths;
        _packs.Dispose();
    }

    private void ApplySearchPaths()
    {
        var (effects, textures) = _packs.SearchPaths();
        _services.ReShade.SetSearchPaths(effects, textures);
    }

    private void OnReShadeChanged()
    {
        if (_rs.OnChanged()) _rsOptionsCache = null;   // the dropdown depends on the current preset only
        // dxgi.dll only tells the NotInstalled states apart (R3); a file probe per Changed is wasted once ReShade runs.
        if (_services.ReShade.State == ReShadeState.NotInstalled) _dxgiPresent = File.Exists(Path.Combine(_gameRoot, "dxgi.dll"));
        ReadReShadeVersionOnce();
    }

    /// <summary>Call after the panel or the docked strip shows / hides. Photo Studio is "open" while either is on screen.
    /// Opening snapshots ReShade; closing restores it (owner ruling O1) and drops a shot still waiting at the gate, so
    /// nothing is taken after close.</summary>
    private void SyncStudioOpen()
    {
        var open = _panelShown || _dockedShown;
        if (open == _studioOpen) return;
        _studioOpen = open;
        if (open)
        {
            _rs.OnStudioOpened();
            return;
        }
        if (_captureGate.Disarm())
        {
            _timeoutLogged = false;
            _quality.SetCapturing(false);   // CaptureNow turned the capture-only boost on for the shot that is dropped
        }
        _rs.OnStudioClosed();
    }

    /// <summary>ReShade's version for the sidecar: read from dxgi.dll ONCE, at the first Ready, then cached — never per
    /// draw or per shot (FileVersionInfo is a file read).</summary>
    private void ReadReShadeVersionOnce()
    {
        if (_reShadeVersionRead || _services.ReShade.State != ReShadeState.Ready) return;
        _reShadeVersionRead = true;
        _reShadeVersion = ReShadeInfo.ReadVersion(Path.Combine(_gameRoot, "dxgi.dll"));
    }

    /// <summary>Per framework tick, BEFORE TickStudio: settles ReShade requests, then lets the capture gate fire (R1).</summary>
    private void TickReShade(float dt)
    {
        _rs.Tick(dt);
        if (_rs.TimedOut && _captureGate.Armed && !_timeoutLogged)
        {
            _timeoutLogged = true;   // once per shot: TimedOut stays true until the next request
            _services.Log.Warning("[PhotoStudio] ReShade did not confirm a switch within 3 s; taking the photo anyway");
        }
        if (!_captureGate.Tick(_rs.Pending)) return;
        _timeoutLogged = false;
        StartFlash();   // the flash marks the shot itself, not the press (the gate may have waited up to 3 s)
        _ = _session.CaptureAsync();
    }

    private void RescanReShadePresets()
    {
        try
        {
            Directory.CreateDirectory(ReShadePresetFolder);
            _rsPresetFiles = ReShadePresets.List(Directory.EnumerateFiles(ReShadePresetFolder, "*.ini"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _rsPresetFiles = Array.Empty<string>();
        }
        _rsOptionsCache = null;
    }

    private void SnapshotReShadeForShot() =>
        _shot = new ShotReShade(_rs.Installed, _services.ReShade.State == ReShadeState.Ready && _rs.Enabled, _rs.PresetFile);

    private ReShadeShot? ShotFor(CaptureResult r)
    {
        if (!_shot.Installed) return null;
        var applied = _shot.On && !Contains(r.Notes, ReShadeInfo.NotReadyNote);
        return new ReShadeShot(_shot.Preset, _reShadeVersion, applied, r.Notes);
    }

    private static bool Contains(IReadOnlyList<string> notes, string note)
    {
        foreach (var n in notes)
            if (n == note) return true;
        return false;
    }
}
