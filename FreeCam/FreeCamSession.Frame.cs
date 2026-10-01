using System;
using System.Numerics;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>Per-frame stepping (from <c>ICameraControl.Frame</c>, once per rendered frame) and input edges.</summary>
internal sealed partial class FreeCamSession
{
    private OrbitState _orbit;
    private FlyState _fly;
    private Vector3 _shownPos;
    private float _shownYaw, _shownPitch;
    private Vector3 _subjectPos;
    private Vector3? _freezeCentre;

    private Vector3 LeashCentre => _freezeCentre ?? _subjectPos;

    private void OnFrame(float dt)
    {
        if (_shield is null) return;
        try
        {
            var (intent, edges) = ScriptedIntent is { } scripted ? (scripted, default(FreeCamEdges)) : _input.Read(_shield);
            HandleEdges(edges);
            if (_control is not null) Step(dt, intent);
        }
        catch (Exception)
        {
            Release(CameraReleaseReason.Error, frameworkEnded: false);   // spec § 7: any exception → release + toast
        }
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
            Exit();   // spec D8: Esc leaves the free camera; the keyboard gate keeps it from opening the game menu
            return;
        }
        if (e.ToggleMode) ToggleMode();
        if (e.ToggleFreeze) ToggleFreeze();
        if (e.Reset) ResetPose();
        if (e.ToggleHint) _settings.SetHintHidden(!_settings.HintHidden);
        if (e.BackToSelf) SetSubject(_p.Snapshot.LocalEntityId);
        if (e.Click is not { } at || Mode != FreeCamMode.Orbit || _pointerOverUi(at.X, at.Y)) return;
        if (_p.Picker.TryPickEntity(at.X, at.Y, out var picked)) SetSubject(picked);
    }

    /// <summary>The subject's position now; a picked character who left hands the subject back to the local player.</summary>
    private Vector3 SubjectPosition(Vector3 fallback)
    {
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
