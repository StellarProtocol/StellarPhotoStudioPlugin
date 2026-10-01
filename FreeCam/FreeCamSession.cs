using System;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.FreeCam;

// Per-frame stepping and input edges: FreeCamSession.Frame.cs.

/// <summary>
/// The free camera (spec §§ 3–4, 7): owns the camera control, the input shield, the freeze token, the look-at handle and
/// the entry hides, and releases all of them through <see cref="Release"/> — the one release path — whatever ends the
/// session (exit, the framework, unload, an exception in the frame code). Main thread.
/// </summary>
internal sealed partial class FreeCamSession : IDisposable
{
    private const VisibilityLayers EntryHideLayers = VisibilityLayers.GameHud | VisibilityLayers.Nameplates;

    private readonly FreeCamPorts _p;
    private readonly FreeCamSettings _settings;
    private readonly Action<FreeCamNotice, CameraReleaseReason> _notify;
    private readonly Func<float, float, bool> _pointerOverUi;
    private readonly FreeCamInput _input = new();
    private readonly Action<float> _onFrame;
    private readonly Action<CameraReleaseReason> _onReleased;
    private readonly Action<bool> _onFreezeChanged;
    private readonly Action<bool> _onCombatChanged;
    private ICameraControl? _control;
    private IInputShieldHandle? _shield;
    private IDisposable? _freeze, _look, _hide;
    private CameraPose _entry;

    public FreeCamSession(FreeCamPorts ports, FreeCamSettings settings, Action<FreeCamNotice, CameraReleaseReason> notify,
        Func<float, float, bool> pointerOverUi)
    {
        _p = ports;
        _settings = settings;
        _notify = notify;
        _pointerOverUi = pointerOverUi;
        _onFrame = OnFrame;
        _onReleased = OnReleased;
        _onFreezeChanged = OnFreezeChanged;
        _onCombatChanged = _ => StateChanged?.Invoke();
    }

    /// <summary>Raised whenever something the HUD / panel shows changed (active, mode, freeze, subject, combat).</summary>
    public event Action? StateChanged;

    public bool Active => _control is not null;
    public FreeCamMode Mode { get; private set; }
    public bool Frozen => _freeze is not null;
    public EntityId Subject { get; private set; }
    public float Distance { get; private set; }
    public float Fov { get; private set; }
    public float Roll { get; private set; }

    /// <summary>Replaces keyboard/mouse input while set (the env-gated self-test only).</summary>
    internal CamIntent? ScriptedIntent { get; set; }

    public bool Enter()
    {
        if (Active) return true;
        if (!_p.Camera.TryAcquire(out var control))
        {
            _notify(_p.Camera.IsOverridden ? FreeCamNotice.Busy : FreeCamNotice.Unavailable, CameraReleaseReason.Disposed);
            return false;
        }
        _control = control;
        _shield = _p.Shield.Shield();
        _input.Prime(_shield);
        _entry = control.GamePose;
        Subject = _p.Snapshot.LocalEntityId;
        _subjectPos = SubjectPosition(CameraMath.ToVec(_entry.Position));
        PlaceAtEntry();
        Mode = FreeCamMode.Orbit;
        if (_settings.EntryHides) _hide = _p.Visibility.Hide(EntryHideLayers);
        if (_settings.LookAt) _look = _p.Camera.LookAtCamera();
        control.Frame += _onFrame;
        _p.Camera.Released += _onReleased;
        _p.Freeze.Changed += _onFreezeChanged;
        _p.Combat.Changed += _onCombatChanged;
        StateChanged?.Invoke();
        return true;
    }

    public void Exit() => Release(CameraReleaseReason.Disposed, frameworkEnded: false);

    public void Dispose() => Release(CameraReleaseReason.PluginUnloaded, frameworkEnded: false);

    public void ToggleFreeze()
    {
        if (!Active) return;
        if (_freeze is not null) EndFreeze();
        else
        {
            _freeze = _p.Freeze.Freeze();
            _freezeCentre = _subjectPos;
        }
        StateChanged?.Invoke();
    }

    public void ToggleMode()
    {
        if (!Active) return;
        if (Mode == FreeCamMode.Orbit)
        {
            _fly = new FlyState(_shownPos, _shownYaw, _shownPitch);
            Mode = FreeCamMode.Fly;
        }
        else
        {
            _orbit = OrbitRig.FromCamera(_shownPos, _shownYaw, _shownPitch, LeashCentre);
            Mode = FreeCamMode.Orbit;
        }
        StateChanged?.Invoke();
    }

    public void ResetPose()
    {
        if (Active) PlaceAtEntry();
    }

    public void SetSubject(EntityId id)
    {
        if (!Active || id.IsNone || !_p.Transforms.TryGetTransform(id, out var p, out _)) return;
        Subject = id;
        _subjectPos = CameraMath.ToVec(p);
        if (_freezeCentre is not null) _freezeCentre = _subjectPos;
        if (Mode == FreeCamMode.Orbit) _orbit = OrbitRig.FromPose(_shownPos, _subjectPos);
        StateChanged?.Invoke();
    }

    public void SetLookAt(bool on)
    {
        _settings.SetLookAt(on);
        if (!Active) return;
        if (on) _look ??= _p.Camera.LookAtCamera();
        else
        {
            _look?.Dispose();
            _look = null;
        }
    }

    /// <summary>Spec § 7: on death the free camera stays and the freeze ends.</summary>
    public void OnLocalDeath()
    {
        if (_freeze is null) return;
        EndFreeze();
        StateChanged?.Invoke();
    }

    private void OnReleased(CameraReleaseReason reason)
    {
        if (_control is { IsActive: false }) Release(reason, frameworkEnded: true);
    }

    private void OnFreezeChanged(bool frozen)
    {
        if (!frozen && _freeze is not null) EndFreeze();   // the framework unfroze (zone change / cutscene)
        StateChanged?.Invoke();
    }

    private void EndFreeze()
    {
        _freeze?.Dispose();
        _freeze = null;
        _freezeCentre = null;
    }

    /// <summary>The one release path. Unsubscribes first, so our own dispose never re-enters through Released.</summary>
    private void Release(CameraReleaseReason reason, bool frameworkEnded)
    {
        if (_control is not { } c) return;
        _control = null;
        c.Frame -= _onFrame;
        _p.Camera.Released -= _onReleased;
        _p.Freeze.Changed -= _onFreezeChanged;
        _p.Combat.Changed -= _onCombatChanged;
        if (!frameworkEnded) c.Dispose();
        EndFreeze();
        _look?.Dispose();
        _look = null;
        _hide?.Dispose();
        _hide = null;
        _shield?.Dispose();
        _shield = null;
        ScriptedIntent = null;
        StateChanged?.Invoke();
        if (reason is CameraReleaseReason.Disposed or CameraReleaseReason.PluginUnloaded) return;
        _notify(reason == CameraReleaseReason.Error ? FreeCamNotice.Error : FreeCamNotice.Released, reason);
    }
}
