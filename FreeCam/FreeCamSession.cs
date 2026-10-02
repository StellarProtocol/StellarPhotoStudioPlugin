using System;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.FreeCam;

// Per-frame stepping and input edges: FreeCamSession.Frame.cs.

/// <summary>
/// The free camera (spec §§ 3–4, 7): owns the camera control, the input shield, the look-at handle and the entry hides,
/// and releases all of them through <see cref="Release"/> — the one release path — whatever ends the session (exit, the
/// framework, unload, an exception in the frame code). The freeze and the posed people belong to the
/// <see cref="StudioScene"/> and outlive the camera (scene-stays spec § 1): the session only toggles the freeze through it
/// and reads the leash centre from it, and remembers its last pose for a re-entry while the scene is set (§ 6). Main thread.
/// </summary>
internal sealed partial class FreeCamSession : IDisposable
{
    private readonly FreeCamPorts _p;
    private readonly FreeCamSettings _settings;
    private readonly FreeCamHost _host;
    private readonly FreeCamInput _input = new();
    private readonly Action<float> _onFrame;
    private readonly Action<CameraReleaseReason> _onReleased;
    private readonly StudioScene _scene;
    private readonly Action _onSceneChanged;
    private readonly Action<bool> _onCombatChanged;
    private ICameraControl? _control;
    private IInputShieldHandle? _shield;
    private IDisposable? _look, _hide;
    private CameraPose _entry;

    public FreeCamSession(FreeCamPorts ports, FreeCamSettings settings, FreeCamHost host, StudioScene scene)
    {
        _p = ports;
        _settings = settings;
        _host = host;
        _scene = scene;
        _onFrame = OnFrame;
        _onReleased = OnReleased;
        _onSceneChanged = () => StateChanged?.Invoke();
        _onCombatChanged = _ => StateChanged?.Invoke();
    }

    /// <summary>Raised whenever something the HUD / panel shows changed (active, mode, freeze, subject, combat).</summary>
    public event Action? StateChanged;

    public bool Active => _control is not null;
    public FreeCamMode Mode { get; private set; }
    /// <summary>The scene's freeze (it outlives the free camera — <see cref="StudioScene.Frozen"/>).</summary>
    public bool Frozen => _scene.Frozen;
    public EntityId Subject { get; private set; }
    /// <summary>The farthest the orbit camera sits from a newly selected subject (closer cameras keep their distance).</summary>
    internal const float FrameDistance = 4f;

    public float Distance { get; private set; }
    public float Fov { get; private set; }
    public float Roll { get; private set; }

    /// <summary>Replaces keyboard/mouse input while set (the env-gated self-test only).</summary>
    internal CamIntent? ScriptedIntent { get; set; }

    /// <summary>Takes the camera. <paramref name="subject"/> = the person to orbit (the Person group's selection; none or
    /// gone = yourself). While the scene is set the camera returns to the pose it had when it left (spec § 6); otherwise
    /// it starts from the game camera.</summary>
    public bool Enter(EntityId subject = default)
    {
        if (Active) return true;
        if (!_p.Camera.TryAcquire(out var control))
        {
            _host.Notify(_p.Camera.IsOverridden ? FreeCamNotice.Busy : FreeCamNotice.Unavailable, CameraReleaseReason.Disposed);
            return false;
        }
        _control = control;
        _shield = _p.Shield.Shield();
        _input.Prime(_shield);
        _rmbDown = false;
        _lookFromOwnWindow = false;
        _entry = control.GamePose;
        Subject = EntrySubject(subject);
        _subjectPos = SubjectPosition(CameraMath.ToVec(_entry.Position));
        PlaceForEntry();
        if (_settings.EntryHides != VisibilityLayers.None) _hide = _p.Visibility.Hide(_settings.EntryHides);
        if (_settings.LookAt) _look = _p.Camera.LookAtCamera();
        control.Frame += _onFrame;
        _p.Camera.Released += _onReleased;
        _scene.Changed += _onSceneChanged;
        _p.Combat.Changed += _onCombatChanged;
        StateChanged?.Invoke();
        return true;
    }

    public void Exit() => Release(CameraReleaseReason.Disposed, frameworkEnded: false);

    public void Dispose() => Release(CameraReleaseReason.PluginUnloaded, frameworkEnded: false);

    /// <summary>Space: freezes the scene around the orbit subject, or unfreezes it (through the scene).</summary>
    public void ToggleFreeze()
    {
        if (!Active) return;
        _scene.ToggleFreeze(_subjectPos);
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
        // Re-picking the subject you're already orbiting (clicking them again, or Backspace while already on
        // yourself) must be a no-op: without this, re-framing recomputed the orbit from the camera's CURRENT
        // position and clamped it to FrameDistance, yanking a deliberately zoomed-out camera in to ≤4 m.
        if (!Active || id.IsNone || id == Subject || !_p.Transforms.TryGetTransform(id, out var p, out _)) return;
        Subject = id;
        _subjectPos = CameraMath.ToVec(p);
        _scene.MoveFreezeCentre(_subjectPos);
        if (Mode == FreeCamMode.Orbit)
        {
            // Frame the new subject: keep the viewing angle but come no further than FrameDistance (a far pick stayed tiny —
            // owner-run smoke 2026-10-02). A closer camera keeps its own distance.
            var o = OrbitRig.FromPose(_shownPos, _subjectPos);
            _orbit = o with { Distance = MathF.Min(o.Distance, FrameDistance) };
        }
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

    private void OnReleased(CameraReleaseReason reason)
    {
        if (_control is { IsActive: false }) Release(reason, frameworkEnded: true);
    }

    /// <summary>The one release path. Unsubscribes first, so our own dispose never re-enters through Released. Returns
    /// only the camera, the shield, the look-at handle and the entry hides — the freeze and the posed people stay with
    /// the scene (scene-stays spec § 1); the pose is remembered for a re-entry while the scene is set (§ 6).
    /// Each disposal step is isolated (spec § 7): an exception from one — e.g. the camera control's own
    /// <c>Dispose()</c> re-raises <c>Released</c> to every other holder of that event, and another plugin's
    /// throwing handler propagates straight back out of that call — must never stop the rest from running. The
    /// shield is disposed FIRST because it blocks every game key; it must never stay held just because a later
    /// step threw.</summary>
    private void Release(CameraReleaseReason reason, bool frameworkEnded)
    {
        if (_control is not { } c) return;
        _control = null;
        c.Frame -= _onFrame;
        _p.Camera.Released -= _onReleased;
        _scene.Changed -= _onSceneChanged;
        _p.Combat.Changed -= _onCombatChanged;
        RememberPose();

        ReleaseStep(() => { _shield?.Dispose(); _shield = null; });
        ReleaseStep(() => { if (!frameworkEnded) c.Dispose(); });
        ReleaseStep(() => { _look?.Dispose(); _look = null; });
        ReleaseStep(() => { _hide?.Dispose(); _hide = null; });

        ScriptedIntent = null;
        StateChanged?.Invoke();
        if (reason is CameraReleaseReason.Disposed or CameraReleaseReason.PluginUnloaded) return;
        _host.Notify(reason == CameraReleaseReason.Error ? FreeCamNotice.Error : FreeCamNotice.Released, reason);
    }

    /// <summary>Runs one release step; an exception is logged and swallowed so the remaining steps still run (isolated
    /// — see Release's doc comment, spec § 7). There is no <see cref="FreeCamNotice"/> case for "a release step failed":
    /// the toast path already reports the release itself (Released/Error) once every step has had its turn.</summary>
    private void ReleaseStep(Action step)
    {
        try { step(); }
        catch (Exception ex) { _host.Warn("[PhotoStudio] free camera: a release step failed: " + ex); }
    }
}
