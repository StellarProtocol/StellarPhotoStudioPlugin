using System;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>Per-frame stepping (from <c>ICameraControl.Frame</c>, once per rendered frame) and input edges.</summary>
internal sealed partial class FreeCamSession
{
    private OrbitState _orbit;
    private FlyState _fly;
    private Vector3 _shownPos;
    private float _shownYaw, _shownPitch;
    private Vector3 _subjectPos;
    private bool _rmbDown, _lookFromOwnWindow;

    /// <summary>Where the free camera is now and which way it looks (lights: drop / move a lamp at the camera); null while
    /// the free camera is off.</summary>
    internal (Vector3 Position, float Yaw)? ShownPose => Active ? (_shownPos, _shownYaw) : null;

    /// <summary>Scene-stays spec § 8: while frozen the leash centre stays the scene's freeze centre.</summary>
    private Vector3 LeashCentre => _scene.FreezeCentre ?? _subjectPos;

    private void OnFrame(float dt)
    {
        if (_shield is null) return;
        try
        {
            var (intent, edges) = ScriptedIntent is { } scripted ? (scripted, default(FreeCamEdges)) : ReadInput(_shield);
            HandleEdges(edges);
            if (_control is not null) Step(dt, intent);
        }
        catch (Exception ex)
        {
            // spec § 7: any exception → release + toast; the toast says "Details are in the log", so log it here.
            _host.Warn("[PhotoStudio] free camera turned off after an error: " + ex);
            Release(CameraReleaseReason.Error, frameworkEnded: false);
        }
    }

    private (CamIntent, FreeCamEdges) ReadInput(IInputShieldHandle h)
    {
        var (intent, edges) = _input.Read(h);
        return (GateOwnWindows(intent, h.Pointer), edges);
    }

    /// <summary>A wheel turn over Photo Studio's own windows (or, while the game UI is shown, the game's), or a
    /// right-button drag that STARTED over them, belongs to the window, not the camera. The hit test runs only on a wheel
    /// turn or a right-button press, never every frame.</summary>
    private CamIntent GateOwnWindows(CamIntent intent, (float X, float Y) pointer)
    {
        if (intent.Looking && !_rmbDown) _lookFromOwnWindow = PointerOverUi(pointer.X, pointer.Y);
        _rmbDown = intent.Looking;
        if (!intent.Looking) _lookFromOwnWindow = false;
        if (_lookFromOwnWindow) intent = intent with { Looking = false, LookX = 0f, LookY = 0f };
        if (intent.Wheel != 0f && PointerOverUi(pointer.X, pointer.Y)) intent = intent with { Wheel = 0f };
        return intent;
    }

    internal void Step(float dt, in CamIntent intent)
    {
        if (_control is not { } c) return;
        _subjectPos = SubjectPosition(_subjectPos);
        var centre = LeashCentre;
        var t = _settings.Tuning;
        var target = Target(intent, t, centre, dt);
        var pos = CameraMath.ProjectIntoSphere(target.Position, centre, t.Leash);
        Roll = CameraMath.StepRoll(Roll, intent.RollAxis, intent.Shift, dt);
        if (intent.Shift && intent.Wheel != 0f) Fov = CameraMath.StepFov(Fov, intent.Wheel);
        var tau = CameraMath.SmoothingToTau(t.Smoothing);
        _shownPos = CameraMath.Damp(_shownPos, pos, tau, dt);
        _shownYaw = CameraMath.DampAngle(_shownYaw, target.Yaw, tau, dt);
        _shownPitch = CameraMath.Damp(_shownPitch, target.Pitch, tau, dt);
        c.SetPose(CameraMath.ToPos(_shownPos), _shownYaw, _shownPitch, Roll);
        if (c.Fov != Fov) c.Fov = Fov;
        Distance = Vector3.Distance(_shownPos, centre);
    }

    private RigOutput Target(in CamIntent intent, RigTuning t, Vector3 centre, float dt)
    {
        if (Mode == FreeCamMode.Fly)
        {
            _fly = FlyRig.Step(_fly, intent, t, centre, dt);
            return new RigOutput(_fly.Position, _fly.Yaw, _fly.Pitch);
        }
        _orbit = OrbitRig.Step(_orbit, intent, t, dt);
        return OrbitRig.Place(_orbit, centre, t.Leash);
    }

    private void HandleEdges(in FreeCamEdges e)
    {
        if (e.Exit)
        {
            // An open "?" popover takes the Esc first; otherwise spec D8: Esc leaves the free camera (the keyboard gate
            // keeps it from opening the game menu).
            if (!_host.DismissModalUi()) Exit();
            return;
        }
        if (e.ToggleMode) ToggleMode();
        if (e.ToggleFreeze) ToggleFreeze();
        if (e.Reset) ResetPose();
        if (e.ToggleHint) _settings.SetHintHidden(!_settings.HintHidden);
        if (e.ToggleGameUi) SetGameUi(!GameUiShown);
        if (e.DropLamp || e.MoveLamp) LampKey?.Invoke(e.MoveLamp);   // lights spec § 2 — the plugin owns the lamps
        if (e.BackToSelf) SetSubject(_p.Snapshot.LocalEntityId);
        if (e.Click is not { } at || Mode != FreeCamMode.Orbit || PointerOverUi(at.X, at.Y)) return;
        if (_p.Picker.TryPickEntity(at.X, at.Y, out var picked)) SetSubject(picked);
    }

    /// <summary>The subject's position now: the posed copy / NPC stand-in's own position while there is one (the real
    /// person is hidden and may walk off — controller decision Q5), else the entity's. A picked character who left hands
    /// the subject back to the local player. Called every frame: <c>TryGetVisiblePosition</c> does not allocate.</summary>
    private Vector3 SubjectPosition(Vector3 fallback)
    {
        if (_p.Posing is { } posing && posing.TryGetVisiblePosition(Subject, out var posed)) return CameraMath.ToVec(posed);
        if (_p.Transforms.TryGetTransform(Subject, out var p, out _)) return CameraMath.ToVec(p);
        var self = _p.Snapshot.LocalEntityId;
        if (Subject == self || !_p.Transforms.TryGetTransform(self, out var s, out _)) return fallback;
        Subject = self;
        StateChanged?.Invoke();
        return CameraMath.ToVec(s);
    }

    /// <summary>Snap (no damping) to the game camera's pose at entry; roll 0, the game's FOV.</summary>
    private void PlaceAtEntry()
    {
        _shownPos = CameraMath.ToVec(_entry.Position);
        _shownYaw = _entry.Yaw;
        _shownPitch = _entry.Pitch;
        _orbit = OrbitRig.FromCamera(_shownPos, _shownYaw, _shownPitch, LeashCentre);
        _fly = new FlyState(_shownPos, _shownYaw, _shownPitch);
        Roll = 0f;
        Fov = CameraMath.ClampFov(_entry.Fov);
    }
}
