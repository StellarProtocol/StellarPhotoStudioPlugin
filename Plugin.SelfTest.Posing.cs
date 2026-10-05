using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Posing;

namespace Stellar.PhotoStudio;

// Unattended posing smoke for the TEST client only (STELLAR_PHOTOSTUDIO_POSING_SELFTEST=1, read once at load; inert
// otherwise). 15 s after entering the world: free camera on → an emote played through the game's own path, then you
// (self-detect: the panel must show that running emote; self-detect-pause: ❚❚ holds it without replaying it) → the
// nearest other player (a copy; skipped — never
// failed — when nobody is within 40 m for 120 s) → the nearest NPC (a generated model; the load is awaited) → free
// camera off. Per person: play, load, pause at 40 %, scrub 75 % → 40 %, first expression held, head Lens + eyes Free
// right, rotate +30°, scene freeze on (posed copies/models freeze too — controller Q4), capture while frozen, freeze off,
// reset. "window=begin/end target=…" bracket each person so the scenario can count the RPCs
// sent meanwhile (framework diagnostics "[Posing.Send]"). Every image is an in-process camera render.
public sealed partial class Plugin
{
    private const string PosingSelfTestEnvVar = "STELLAR_PHOTOSTUDIO_POSING_SELFTEST";
    private const float PzFindLimit = 120f, PzLoadLimit = 15f, PzGap = 1.5f;
    private bool _pzOn;
    private int _pzStep, _pzPhase;
    private float _pzClock, _pzDueAt, _pzSince;
    private string _pzWho = "";
    private int _pzGameEmote;
    private float _pzHeldUi, _pzHeldFirst;
    private bool _pzHeldFirstOk;

    private void ArmPosingSelfTest()
    {
        _pzOn = Environment.GetEnvironmentVariable(PosingSelfTestEnvVar) == "1";
        if (_pzOn) _services.Log.Info("[PhotoStudio] posing selftest armed");
        ArmPosingSweep();
    }

    private void TickPosingSelfTest(float dt)
    {
        if (!_pzOn || _pzStep < 0) return;
        if (!InWorld()) { _pzClock = 0f; return; }
        _pzClock += dt;
        if (_pzClock < SelfTestDelay || _pzClock < _pzDueAt) return;
        RunPosingStep();
    }

    private void RunPosingStep()
    {
        switch (_pzStep)
        {
            case 0: PzLog("enter", _freeCam.Enter()); _pzStep = 1; PzDue(1f); break;
            case 1: PzProbeStart(loop: false); _pzStep = 2; break;
            case 2: if (PzProbeTick()) { PzProbeStart(loop: true); _pzStep = 3; } break;
            case 3: if (PzProbeTick()) { _pzStep = 4; PzDue(2f); } break;
            case 4: PzGameEmote(); _pzStep = 5; PzDue(1f); break;
            case 5: PzBegin("self", _services.CombatSnapshot.LocalEntityId, next: 6); break;
            case 6: if (PzDetectPhase()) _pzStep = 7; break;
            case 7: if (PzPersonPhase()) PzWaitFrom(8); break;
            case 8: PzFind("clone", PersonKind.Player, next: 9, skip: 10); break;
            case 9: if (PzPersonPhase()) PzWaitFrom(PzAfterClone); break;
            case 10: PzFind("npc", PersonKind.Npc, next: 11, skip: 12); break;
            case 11: if (PzPersonPhase()) _pzStep = 12; break;
            case PzSweepStep: PzSweepStart(); break;
            case PzSweepStep + 1: PzSweepTick(); break;
            case PzSweepStep + 2: PzSweepScan(); break;
            default: PzFinish(); break;
        }
    }

    private void PzWaitFrom(int step)
    {
        _pzStep = step;
        _pzSince = _pzClock;
    }

    private void PzBegin(string who, EntityId id, int next)
    {
        _pzWho = who;
        _pzPhase = 0;
        _freeCam.SetSubject(id);
        _services.Log.Info($"[PhotoStudio] posing selftest window=begin target={who} person={_posingCtl.Person?.Name} kind={_posingCtl.Person?.Kind}");
        _pzStep = next;
        PzDue(1f);
    }

    /// <summary>Bounded wait for someone of <paramref name="kind"/> within 40 m: one look every 2 s, at most 120 s. A miss
    /// is logged as skipped (it depends on who is around), never as a failure.</summary>
    private void PzFind(string who, PersonKind kind, int next, int skip)
    {
        foreach (var p in _services.Posing.NearbyPeople(PosingController.PeopleRadius))
            if (p.Kind == kind) { if (kind == PersonKind.Player) _pzCloned = p.Id; PzBegin(who, p.Id, next); return; }
        if (_pzClock - _pzSince < PzFindLimit) { PzDue(2f); return; }
        _services.Log.Info($"[PhotoStudio] posing selftest step={who} skipped reason=nobody-within-{PosingController.PeopleRadius:0}m");
        _pzStep = skip;
    }

    /// <summary>One step for the current person; true once the person is done (reset).</summary>
    private bool PzPersonPhase()
    {
        var c = _posingCtl;
        switch (_pzPhase++)
        {
            case 0:
                _pzSince = _pzClock;
                c.Play(PzAction());
                PzLog(_pzWho + "-play", c.LastResult is PoseResult.Applied or PoseResult.Loading, $"action={c.State.Action?.Id} result={c.LastResult}");
                PzDue(0.25f);
                return false;
            case 1: return PzAwaitLoad();
            case 2: c.SetMoment(0.4f); PzStepDone("-pause", !c.State.Playing && c.TargetState == PoseTargetState.Ready); return false;
            case 3: c.SetMoment(0.75f); c.SetMoment(0.4f); PzStepDone("-scrub", c.TargetState == PoseTargetState.Ready); return false;
            case 4: c.CycleExpression(1); PzStepDone("-face", c.State.ExpressionIndex == 0, $"expressions={c.Expressions.Count} face={c.ExpressionName}"); return false;
            case 5:
                c.SetLook(LookPart.Head, LookMode.Lens);
                c.SetLook(LookPart.Eyes, LookMode.Free);
                c.Aim(LookPart.Eyes, 1, 0, PosingController.AimSteps[2]);
                PzStepDone("-look", c.TargetState == PoseTargetState.Ready);
                return false;
            case 6: c.SetYaw(30f); PzStepDone("-yaw", c.TargetState == PoseTargetState.Ready); return false;
            case 7: _freeCam.ToggleFreeze(); PzStepDone("-freeze", _freeCam.Frozen && c.TargetState == PoseTargetState.Ready); return false;
            case 8: CaptureNow(); PzLog(_pzWho + "-capture-requested", true); PzDue(5f); return false;
            case 9: _freeCam.ToggleFreeze(); PzStepDone("-unfreeze", !_freeCam.Frozen && c.TargetState == PoseTargetState.Ready); return false;
            default: return PzEndPerson();
        }
    }

    /// <summary>Owner bug 2026-10-02: before the self window, play an emote through the GAME's own path (the emote
    /// service = the expression VM's PlayAction, as the free-camera self-test does — never through the posing
    /// controller), so selecting yourself must DETECT it. A looping emote is preferred so it is still running.</summary>
    private void PzGameEmote()
    {
        var unlocked = _services.Emotes.Unlocked;
        var id = unlocked.Count > 0 ? unlocked[0].Id : 9011;   // 9011 = Wave (probe-proven)
        foreach (var e in unlocked)
            if (e.Looping) { id = e.Id; break; }
        _pzGameEmote = id;
        var result = _services.Emotes.PlayAsync(id).Result;
        PzLog("self-game-emote", result == EmoteResult.Played, $"id={id} result={result} unlocked={unlocked.Count}");
    }

    /// <summary>Phase 0: the panel shows the running emote (detected id == the one the game played). Phase 1: ❚❚, then a
    /// first game-truth read. Phase 2 (~1 s later): a second read — the hold is real only if the game's moment did not
    /// move (two reads within 0.01) and sits where the panel paused it (within 0.02). True once done.</summary>
    private bool PzDetectPhase()
    {
        var c = _posingCtl;
        var me = _services.CombatSnapshot.LocalEntityId;
        switch (_pzPhase++)
        {
            case 0:
                c.PollCurrentAction();
                var s = c.State;
                PzStepDone("-detect", s.Detected && s.Action?.Id == _pzGameEmote,
                    $"played={_pzGameEmote} detected={s.Action?.Id} name={s.Action?.Name} playing={s.Playing} moment={s.Moment:F2}");
                return false;
            case 1:
                c.TogglePlay();
                _pzHeldUi = c.State.Moment;
                _pzHeldFirstOk = _services.Posing.TryGetCurrentAction(me, out var firstId, out _pzHeldFirst) && firstId == _pzGameEmote;
                PzDue(1f);
                return false;
            default:
                var held = _services.Posing.TryGetCurrentAction(me, out var id, out var moment);
                var ok = !c.State.Playing && _pzHeldFirstOk && held && id == _pzGameEmote &&
                         Math.Abs(moment - _pzHeldFirst) < 0.01f && Math.Abs(moment - _pzHeldUi) < 0.02f;
                PzStepDone("-detect-pause", ok,
                    $"id={id} first={_pzHeldFirst:F3} after1s={moment:F3} ui={_pzHeldUi:F3} state={c.TargetState}");
                _pzPhase = 0;   // the regular self phases (our own pick) follow
                return true;
        }
    }

    private bool PzAwaitLoad()
    {
        if (_posingCtl.Loading && _pzClock - _pzSince < PzLoadLimit)
        {
            _pzPhase = 1;   // stay on this step: one look every 0.25 s, at most 15 s
            PzDue(0.25f);
            return false;
        }
        PzStepDone("-load", _posingCtl.TargetState == PoseTargetState.Ready, $"waited={_pzClock - _pzSince:F2}s state={_posingCtl.TargetState}");
        return false;
    }

    private bool PzEndPerson()
    {
        _posingCtl.ResetPerson();
        PzLog(_pzWho + "-reset", _posingCtl.TargetState == PoseTargetState.Idle);
        _services.Log.Info($"[PhotoStudio] posing selftest window=end target={_pzWho}");
        PzDue(2f);
        return true;
    }

    private void PzFinish()
    {
        var wasSet = _scene.IsSet;
        _freeCam.Exit();
        if (wasSet) LogSceneKept(PzLog);   // proof the exit kept the scene (Plugin.SelfTest.cs)
        else PzLog("scene-kept-skipped", true, "nothing set at exit");
        ResetScene();   // scene-stays: leaving the free camera keeps the scene (posing stays available); Reset scene ends it
        PzLog("exit", SceneCleanAfterReset());
        _services.Log.Info("[PhotoStudio] posing selftest DONE");
        _pzStep = -1;
    }

    private EmoteInfo PzAction()
    {
        var unlocked = _services.Emotes.Unlocked;
        foreach (var e in unlocked)
            if (e.Id == 9020) return e;   // "Dance I", the probe's action (4.80 s)
        foreach (var e in unlocked)
            if (!e.Looping) return e;
        return new EmoteInfo(9011, "Wave", "", false);   // probe-proven fallback
    }

    /// <summary>Called from OnCaptureResult: the capture taken of the posed person.</summary>
    private void PosingSelfTestCaptured(CaptureResult r)
    {
        if (_pzOn && _pzStep >= 0) PzLog(_pzWho + "-capture", r.Success, $"path={r.Path} size={r.Width}x{r.Height}");
    }

    private void PzStepDone(string suffix, bool ok, string detail = "")
    {
        PzLog(_pzWho + suffix, ok, detail);
        PzDue(PzGap);
    }

    private void PzDue(float seconds) => _pzDueAt = _pzClock + seconds;

    private void PzLog(string step, bool ok, string detail = "") =>
        _services.Log.Info($"[PhotoStudio] posing selftest step={step} ok={ok} {detail}".TrimEnd());
}
