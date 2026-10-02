using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Posing;

namespace Stellar.PhotoStudio;

// Posing self-test COPY SWEEP (TEST client only; inert unless BOTH STELLAR_PHOTOSTUDIO_POSING_SELFTEST=1 and
// STELLAR_PHOTOSTUDIO_POSING_SWEEP=1). After the regular clone person, every other player nearby
// (every loaded player within PzSweepRadius = the controller's far-pick radius, rescanned for newcomers for up to PzSweepWindow, at most
// PzSweepMax copies) is taken as a photo copy in turn: play → copy Ready → capture (posed) → reset → capture (real
// player shown again). Why: the framework's Male-idle copy guard (regression clone-nre-male-null-ridetpl) fires only on
// an idle MALE source, and the public posing surface carries no gender — so the sweep copies everyone nearby and the
// framework's own diagnostics line ([Posing] clone guard gender=… normalised=…, STELLAR_DIAGNOSTICS=1) says which
// copy was the Male one. Log grammar: [PhotoStudio] posing selftest step=sweep-<n>-<what> ok=<bool> person=<name>.
public sealed partial class Plugin
{
    private const string PosingSweepEnvVar = "STELLAR_PHOTOSTUDIO_POSING_SWEEP";
    private const int PzSweepMax = 10;
    private const float PzSweepRadius = PosingController.SubjectSearchRadius, PzSweepWindow = 180f, PzSweepRescan = 15f;
    private readonly List<PersonInfo> _pzSweep = new();
    private readonly HashSet<EntityId> _pzSweepSeen = new();
    private bool _pzSweepOn;
    private EntityId _pzCloned;
    private int _pzSweepAt, _pzSweepCopies;
    private float _pzSweepBegan;

    private void ArmPosingSweep()
    {
        _pzSweepOn = _pzOn && Environment.GetEnvironmentVariable(PosingSweepEnvVar) == "1";
        if (_pzSweepOn) _services.Log.Info("[PhotoStudio] posing selftest sweep armed");
    }

    /// <summary>The step after the regular clone person: the sweep when armed, else straight on to the NPC.</summary>
    private int PzAfterClone => _pzSweepOn ? PzSweepStep : 10;

    private const int PzSweepStep = 20;

    private void PzSweepStart()
    {
        _pzSweepSeen.Clear();
        _pzSweepCopies = 0;
        _pzSweepBegan = _pzClock;
        PzSweepScan();
    }

    // One look for players not yet copied (every loaded player model within PzSweepRadius, nearest first); when none is
    // new, wait PzSweepRescan and look again until PzSweepWindow has passed or PzSweepMax copies were made.
    private void PzSweepScan()
    {
        _pzSweep.Clear();
        foreach (var p in _services.Posing.NearbyPeople(PzSweepRadius))
            if (p.Kind == PersonKind.Player && !p.Id.Equals(_pzCloned) && _pzSweepSeen.Add(p.Id)) _pzSweep.Add(p);
        _services.Log.Info($"[PhotoStudio] posing selftest sweep scan new={_pzSweep.Count} t={_pzClock - _pzSweepBegan:F0}s");
        _pzSweepAt = 0;
        PzSweepNext();
    }

    private void PzSweepNext()
    {
        if (_pzSweepAt < _pzSweep.Count && _pzSweepCopies < PzSweepMax)
        {
            _pzSweepCopies++;
            PzBegin($"sweep-{_pzSweepCopies - 1}", _pzSweep[_pzSweepAt].Id, next: PzSweepStep + 1);
            return;
        }
        if (_pzSweepCopies < PzSweepMax && _pzClock - _pzSweepBegan < PzSweepWindow)
        {
            _pzStep = PzSweepStep + 2;   // rescan later
            PzDue(PzSweepRescan);
            return;
        }
        _services.Log.Info($"[PhotoStudio] posing selftest sweep done copies={_pzSweepCopies}");
        PzWaitFrom(10);
        PzDue(1f);
    }

    private void PzSweepTick()
    {
        var c = _posingCtl;
        var name = $"person={c.Person?.Name}";
        switch (_pzPhase++)
        {
            case 0:
                _pzSince = _pzClock;
                c.Play(PzAction());
                PzLog(_pzWho + "-play", c.LastResult is PoseResult.Applied or PoseResult.Loading, $"{name} action={c.State.Action?.Id} result={c.LastResult}");
                PzDue(0.25f);
                return;
            case 1:
                if (c.Loading && _pzClock - _pzSince < PzLoadLimit) { _pzPhase = 1; PzDue(0.25f); return; }
                PzStepDone("-load", c.TargetState == PoseTargetState.Ready, $"{name} waited={_pzClock - _pzSince:F2}s state={c.TargetState}");
                return;
            case 2: CaptureNow(); PzLog(_pzWho + "-capture-requested", true, name); PzDue(5f); return;
            case 3:
                c.ResetPerson();
                PzLog(_pzWho + "-reset", c.TargetState == PoseTargetState.Idle, name);
                PzDue(1.5f);
                return;
            case 4: CaptureNow(); PzLog(_pzWho + "-after-reset-capture-requested", true, name); PzDue(5f); return;
            default:
                _services.Log.Info($"[PhotoStudio] posing selftest window=end target={_pzWho}");
                _pzSweepAt++;
                PzSweepNext();
                return;
        }
    }
}
