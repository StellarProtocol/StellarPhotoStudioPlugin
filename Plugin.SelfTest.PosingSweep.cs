using System;
using System.Collections.Generic;
using System.Globalization;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Posing;

namespace Stellar.PhotoStudio;

// Posing self-test COPY SWEEP (TEST client only; inert unless BOTH STELLAR_PHOTOSTUDIO_POSING_SELFTEST=1 and
// STELLAR_PHOTOSTUDIO_POSING_SWEEP=1, or =<seconds> for a longer look-out). After the regular clone person, every other player nearby
// (every loaded player within PzSweepRadius = the controller's far-pick radius, or STELLAR_PHOTOSTUDIO_POSING_SWEEP_RADIUS metres, rescanned for newcomers for up to the window, at most
// PzSweepMax copies) is taken as a photo copy in turn: play → copy Ready → capture (posed) → reset → capture (real
// player shown again). Why: the framework's Male-idle copy guard (regression clone-nre-male-null-ridetpl) fires only on
// an idle MALE source, and the public posing surface carries no gender — so the sweep copies everyone nearby and the
// framework's own diagnostics line ([Posing] clone guard gender=… normalised=…, STELLAR_DIAGNOSTICS=1) says which
// copy was the Male one. Log grammar: [PhotoStudio] posing selftest step=sweep-<n>-<what> ok=<bool> person=<name>.
public sealed partial class Plugin
{
    private const string PosingSweepEnvVar = "STELLAR_PHOTOSTUDIO_POSING_SWEEP";
    // Optional sweep radius in metres (5..PzSweepRadius); unset = PzSweepRadius. A tight radius keeps every copied player
    // inside the game's own draw distance, so the after-reset capture can show the real player standing there again.
    private const string PosingSweepRadiusEnvVar = "STELLAR_PHOTOSTUDIO_POSING_SWEEP_RADIUS";
    private const int PzSweepMax = 10;
    private const float PzSweepRadius = PosingController.SubjectSearchRadius, PzSweepWindow = 180f, PzSweepRescan = 15f;
    private readonly List<PersonInfo> _pzSweep = new();
    private readonly HashSet<EntityId> _pzSweepSeen = new();
    private bool _pzSweepOn;
    private EntityId _pzCloned;
    private int _pzSweepAt, _pzSweepCopies;
    private float _pzSweepBegan, _pzSweepWindow = PzSweepWindow, _pzSweepRadius = PzSweepRadius;

    private void ArmPosingSweep()
    {
        // "1" = the default look-out window; a larger whole number = that many seconds (a quiet spot needs longer).
        var raw = Environment.GetEnvironmentVariable(PosingSweepEnvVar);
        var parsed = int.TryParse(raw, out var secs);
        _pzSweepOn = _pzOn && parsed && secs >= 1;
        _pzSweepWindow = _pzSweepOn && secs > 1 ? Math.Min(secs, 900) : PzSweepWindow;
        _pzSweepRadius = float.TryParse(Environment.GetEnvironmentVariable(PosingSweepRadiusEnvVar), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var r) && r >= 5f
            ? Math.Min(r, PzSweepRadius) : PzSweepRadius;
        if (_pzSweepOn) _services.Log.Info($"[PhotoStudio] posing selftest sweep armed window={_pzSweepWindow:0}s radius={_pzSweepRadius:0}m");
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
    // new, wait PzSweepRescan and look again until the window has passed or PzSweepMax copies were made.
    private void PzSweepScan()
    {
        _pzSweep.Clear();
        foreach (var p in _services.Posing.NearbyPeople(_pzSweepRadius))
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
        if (_pzSweepCopies < PzSweepMax && _pzClock - _pzSweepBegan < _pzSweepWindow)
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
