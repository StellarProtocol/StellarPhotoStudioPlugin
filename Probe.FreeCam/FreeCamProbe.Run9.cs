using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Panda.ZGame;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 9 (2026-10-02, owner "B and go with A if b not good"): feasibility of ONE global time pause
/// (<c>UnityEngine.Time.timeScale = 0</c>) instead of the per-driver scene freeze. Static finding first: the game has no
/// world-pause API of its own (its photo mode pauses only the posed actor via <c>SetActionPersistTime</c>), and
/// <c>GameUpdater.Update</c> feeds <c>Game.Update</c> the SCALED <c>Time.deltaTime</c>; skill shows read
/// <c>ZTimeUtils.FrameTime</c> (scaled <c>Time.timeAsDouble</c>) unless <c>isRealTime_</c>; the heartbeat runs on
/// <c>DateTime.UtcNow</c>. The game itself writes <c>timeScale = 1</c> from <c>ZTimeScaleShowInfo.OnStop</c> /
/// <c>ZCurveTimeScaleShowInfo.OnStop</c> (hit-stop), so the probe re-asserts 0 every Update + LateUpdate and counts the
/// game's writes. Modes (<c>STELLAR_TIMEPAUSE_MODE</c>): <c>town</c> (90 s pause with the framework free camera, a self
/// skill/emote/effect, captures, network + framework-tick counters, blend test), <c>long</c> (300 s pause, network only),
/// <c>field</c> (teleport to a field point and run the town sequence near monsters, then home).
/// </summary>
public sealed partial class FreeCamProbe
{
    private static string R9Mode => Environment.GetEnvironmentVariable("STELLAR_TIMEPAUSE_MODE") is { Length: > 0 } m ? m : "town";

    // ---- pause state (re-asserted per frame) --------------------------------------------------------------------
    private bool _r9Paused;
    private float _r9Prev = 1f;
    private float _r9PausedAt, _r9Deadline;
    private int _r9Frames, _r9DtPositive, _r9WritesAtUpdate, _r9WritesAtLate, _r9FwTicks, _r9CamFrames, _r9CamZeroDt, _r9Chat, _r9Combat;
    private float _r9MaxDt, _r9LastWrite, _r9CamDtSum, _r9CamDtMax;
    private ICameraControl? _r9Cam;
    private Func<float, CameraPose?>? _r9CamDriver;
    private readonly List<string> _r9PostDt = new();
    private int _r9PostFrames = -1;

    private IEnumerator StepSetup9()
    {
        Log($"R9 mode={R9Mode} self={_selfUuid} census {R7Census()}");
        R9TimeState("boot");
        R9Census();
        yield break;
    }

    /// <summary>Counters + per-frame hooks for the run step (armed here, not in the setup step: RunOne releases every
    /// armed key after each step).</summary>
    private void R9ArmCounters()
    {
        _services.Framework.Update += R9OnFwTick;
        _services.Chat.MessageReceived += R9OnChat;
        _services.CombatEvents.CombatEventOccurred += R9OnCombat;
        Arm("r9.counters", () =>
        {
            _services.Framework.Update -= R9OnFwTick;
            _services.Chat.MessageReceived -= R9OnChat;
            _services.CombatEvents.CombatEventOccurred -= R9OnCombat;
        });
        Log($"R9 LUA net-wrap: {Lua(R9NetWrapLua)}");
        ProbeTicks.FrameTick = R9Frame;
        ProbeTicks.LateTick = R9Late;
    }

    private IEnumerator StepRun9()
    {
        var epoch = _sceneEpoch;
        R9ArmCounters();
        switch (R9Mode)
        {
            case "long":
                yield return R9LongPause(epoch, 300f);
                break;
            case "field":
                yield return R9GoField();
                // the framework raises SceneChanged late (when its tick gate clears) — wait for it, then re-arm
                // (ReleaseAll on that scene change drops every armed key, the counters included)
                var ep0 = _sceneEpoch; var tw = Time.realtimeSinceStartup;
                while (ep0 == _sceneEpoch && Time.realtimeSinceStartup - tw < 20f) yield return Wait(0.5f);
                yield return Wait(2f);
                epoch = _sceneEpoch;
                R9ArmCounters();
                Log($"R9 field census {R7Census()}");
                R7AutoBattle(true);
                var t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 20f && !Aborted(epoch))
                {
                    yield return Wait(5f);
                    Log($"R9 field wait-combat +{Time.realtimeSinceStartup - t0:F0}s {R7Census()}");
                    if (R7Combat(SelfEntity())) break;
                }
                yield return R9PauseTest("field", _sceneEpoch, 35f);
                R7AutoBattle(false);
                break;
            default:
                yield return R9PauseTest("town", _sceneEpoch, 45f);
                break;
        }
    }

    private IEnumerator R9GoField()
    {
        var unlocked = Lua("local vm=Z.VMMgr.GetVM('map'); local o={}; for _,id in ipairs({" +
                           string.Join(",", R8Fields.Select(f => f.Tp)) + "}) do if vm.CheckTransferPointUnlock(id) then o[#o+1]=tostring(id) end end; return table.concat(o,',')");
        Log($"R9 FIELD unlocked points: {unlocked}");
        var pick = R8Fields.FirstOrDefault(f => unlocked.Contains(f.Tp.ToString()));
        if (pick.Tp == 0) { Log("R9 FIELD no field point unlocked"); yield break; }
        yield return R7Teleport(pick.Scene, pick.Tp, "r9 field");
    }

    private IEnumerator StepEnd9()
    {
        R9Resume("end step");
        R9ReleaseCam();
        Log($"R9 LUA net counters final: {Lua(R9NetReadLua)}");
        Log($"R9 SENDS total {SendsTotal()}");
        if (R9Mode == "field") yield return R8GoHome("r9 field return");
        ProbeTicks.FrameTick = null;
        ProbeTicks.LateTick = null;
        Release("r9.counters");
        R9TimeState("end");
    }

    // ---- the pause --------------------------------------------------------------------------------------------

    private void R9Pause(string why, float maxSeconds)
    {
        _r9Prev = Time.timeScale;
        _r9Frames = _r9DtPositive = _r9WritesAtUpdate = _r9WritesAtLate = 0;
        _r9MaxDt = 0f;
        _r9LastWrite = 0f;
        _r9Last.Clear(); _r9SnapEvents = _r9VisSteps = _r9AttrSteps = 0; _r9MaxSnap = 0f;
        Time.timeScale = 0f;
        _r9Paused = true;
        _r9PausedAt = Time.realtimeSinceStartup;
        _r9Deadline = _r9PausedAt + maxSeconds + 15f;
        Arm("r9.pause", () => R9Resume("release"));
        Log($"R9 PAUSE ON ({why}) prevTimeScale={_r9Prev:F3} now={Time.timeScale:F3} frame={Time.frameCount} watchdog={maxSeconds + 15f:F0}s");
    }

    private void R9Resume(string why)
    {
        if (!_r9Paused) return;
        _r9Paused = false;
        var restore = _r9Prev > 0.0001f ? _r9Prev : 1f;
        Time.timeScale = restore;
        _r9PostFrames = 0;
        _r9PostDt.Clear();
        Log($"R9 PAUSE OFF ({why}) after {Time.realtimeSinceStartup - _r9PausedAt:F1}s restored timeScale={restore:F3} " +
            $"frames={_r9Frames} framesWithDeltaTime>0={_r9DtPositive} maxDt={_r9MaxDt:F4} gameWritesSeenAtUpdate={_r9WritesAtUpdate} " +
            $"atLateUpdate={_r9WritesAtLate} lastGameWrite={_r9LastWrite:F3} {R9SnapText()}");
    }

    /// <summary>Unity Update (probe runner): counts frames whose scaled delta is not 0 while paused and any timeScale the
    /// game wrote since our last re-assert; re-asserts; enforces the watchdog. After resume, logs the first 10 frames' dt.</summary>
    private void R9Frame()
    {
        if (_r9Paused)
        {
            _r9Frames++;
            var dt = Time.deltaTime;
            if (dt > 0f) { _r9DtPositive++; _r9MaxDt = Math.Max(_r9MaxDt, dt); }
            var ts = Time.timeScale;
            if (ts != 0f) { _r9WritesAtUpdate++; _r9LastWrite = ts; Time.timeScale = 0f; if (_r9WritesAtUpdate <= 5) Log($"R9 GAME-WRITE seen at Update f={Time.frameCount} timeScale={ts:F3} dt={dt:F4}"); }
            if (_r9UniformDt) Cinemachine.CinemachineCore.UniformDeltaTimeOverride = Time.unscaledDeltaTime;
            if (_r9Frames % 3 == 0) R9SnapSample();
            if (Time.realtimeSinceStartup > _r9Deadline) { Log("R9 WATCHDOG fired"); R9Resume("watchdog"); }
        }
        else if (_r9PostFrames >= 0 && _r9PostFrames < 10)
        {
            _r9PostDt.Add($"{Time.deltaTime:F4}/{Time.unscaledDeltaTime:F4}");
            if (++_r9PostFrames == 10) Log($"R9 POST-RESUME first 10 frames dt/unscaled: {string.Join(" ", _r9PostDt)}");
        }
    }

    // per-entity drawn-position steps while paused (does a fast mover snap even though dt = 0?)
    private readonly Dictionary<long, (Vector3 Vis, Vector3 Attr)> _r9Last = new();
    private int _r9SnapEvents, _r9VisSteps, _r9AttrSteps;
    private float _r9MaxSnap;

    private void R9SnapSample()
    {
        try
        {
            foreach (var x in AllEntities(60f))
            {
                if (x.Kind is not ("CharEnt" or "NpcEnt" or "MonsterEnt" or "PetEnt" or "VehicleEnt")) continue;
                var m = LiveModel(EntByUuid(x.Uuid));
                if (m == null) continue;
                var vis = m.ModelGoComp?.Position ?? Vector3.zero;
                var attr = m.GetAttrGoPosition();
                if (_r9Last.TryGetValue(x.Uuid, out var prev))
                {
                    var dv = Vector3.Distance(prev.Vis, vis);
                    if (dv > 0.01f) _r9VisSteps++;
                    if (Vector3.Distance(prev.Attr, attr) > 0.01f) _r9AttrSteps++;
                    if (dv > 0.5f)
                    {
                        _r9SnapEvents++;
                        _r9MaxSnap = Math.Max(_r9MaxSnap, dv);
                        if (_r9SnapEvents <= 8) Log($"R9 SNAP while paused f={Time.frameCount} {x.Kind}:{x.Uuid} drawn jumped {dv:F2}m (attr moved {Vector3.Distance(prev.Attr, attr):F2}m, |drawn-attr| now {Vector3.Distance(vis, attr):F2}m)");
                    }
                }
                _r9Last[x.Uuid] = (vis, attr);
            }
        }
        catch { }
    }

    private string R9SnapText() => $"snapsOver0.5m={_r9SnapEvents} maxSnap={_r9MaxSnap:F2}m drawnStepSamples={_r9VisSteps} attrStepSamples={_r9AttrSteps}";

    private void R9Late()
    {
        if (!_r9Paused) return;
        var ts = Time.timeScale;
        if (ts == 0f) return;
        _r9WritesAtLate++;
        _r9LastWrite = ts;
        Time.timeScale = 0f;
        if (_r9WritesAtLate <= 5) Log($"R9 GAME-WRITE seen at LateUpdate f={Time.frameCount} timeScale={ts:F3}");
    }

    private void R9OnFwTick(float dt) => _r9FwTicks++;
    private void R9OnChat(ChatMessage m) => _r9Chat++;
    private void R9OnCombat(CombatEvent e) => _r9Combat++;

    // ---- free camera (the framework's real ICameraOverride: Frame = FreeCameraFrameDriver, unscaled dt) ----------

    private bool R9AcquireCam(string why)
    {
        if (_r9Cam != null) return true;
        if (!_services.CameraOverride.TryAcquire(out var ctl)) { Log($"R9 CAM acquire FAILED ({why}) overridden={_services.CameraOverride.IsOverridden}"); return false; }
        _r9Cam = ctl;
        ctl.Frame += R9OnCamFrame;
        Arm("r9.cam", R9ReleaseCam);
        Log($"R9 CAM acquired ({why}) gamePose={R9Pose(ctl.GamePose)}");
        return true;
    }

    private void R9ReleaseCam()
    {
        var c = _r9Cam;
        if (c == null) return;
        _r9Cam = null;
        _r9CamDriver = null;
        try { c.Frame -= R9OnCamFrame; c.Dispose(); } catch (Exception ex) { Log($"R9 CAM dispose FAILED {ex.GetType().Name}: {ex.Message}"); }
        Log("R9 CAM released");
    }

    private void R9OnCamFrame(float dt)
    {
        _r9CamFrames++;
        _r9CamDtSum += dt;
        _r9CamDtMax = Math.Max(_r9CamDtMax, dt);
        if (dt <= 0f) _r9CamZeroDt++;
        var drive = _r9CamDriver;
        var c = _r9Cam;
        if (drive == null || c == null) return;
        if (drive(dt) is { } p) c.SetPose(p.Position, p.Yaw, p.Pitch, p.Roll);
    }

    private static string R9Pose(CameraPose p) => $"pos=({p.Position.X:F2},{p.Position.Y:F2},{p.Position.Z:F2}) yaw={p.Yaw:F1} pitch={p.Pitch:F1} fov={p.Fov:F1}";

    private void R9ResetCamCounters() { _r9CamFrames = _r9CamZeroDt = 0; _r9CamDtSum = _r9CamDtMax = 0f; }

    private string R9CamCounters() =>
        $"camFrames={_r9CamFrames} zeroDt={_r9CamZeroDt} dtSum={_r9CamDtSum:F2}s dtMax={_r9CamDtMax:F3}";

    /// <summary>A look-at pose from <paramref name="eye"/> to <paramref name="target"/> (pitch positive = down).</summary>
    private static CameraPose R9LookPose(Vector3 eye, Vector3 target, float fov)
    {
        var e = Quaternion.LookRotation(target - eye).eulerAngles;
        var pitch = e.x > 180f ? e.x - 360f : e.x;
        return new CameraPose(new Position3D(eye.x, eye.y, eye.z), e.y, pitch, 0f, fov);
    }

    // ---- Lua network instrumentation (wraps the game's ConnectMgr instance methods; restored at the end) ---------

    private const string R9NetWrapLua =
        "if rawget(_G,'__r9net') == nil then rawset(_G,'__r9net',{disc=0,recon=0,dlg=0,fail=0}); local cm=Z.ConnectMgr; " +
        "local o1=cm.onDisconnect; cm.onDisconnect=function(s,ch) __r9net.disc=__r9net.disc+1; return o1(s,ch) end; " +
        "local o2=cm.asyncReconnect; cm.asyncReconnect=function(s,ch) __r9net.recon=__r9net.recon+1; return o2(s,ch) end; " +
        "local o3=cm.openDisconnectDlg; cm.openDisconnectDlg=function(s,ch) __r9net.dlg=__r9net.dlg+1; return o3(s,ch) end; " +
        "local o4=cm.openConnectFailedDlg; cm.openConnectFailedDlg=function(s,f) __r9net.fail=__r9net.fail+1; return o4(s,f) end; end; " +
        "return 'wrapped'";

    private const string R9NetReadLua =
        "local n=rawget(_G,'__r9net') or {}; local st=Z.ServerTime; " +
        "local function s(f) local ok,v=pcall(f); return ok and tostring(v) or ('E:'..tostring(v)) end; " +
        "return 'disc='..tostring(n.disc)..' recon='..tostring(n.recon)..' dlg='..tostring(n.dlg)..' fail='..tostring(n.fail)" +
        "..' serverMs='..s(function() return st:GetServerTime() end)..' delay='..s(function() return st:GetDelayTime() end)" +
        "..' lastEchoSend='..s(function() return st:GetWorldLastEchoSendTime() end)..' echoPending='..s(function() return st:IsEchoSentNotReceived() end)" +
        "..' scene='..s(function() return Z.StageMgr.GetCurrentSceneId() end)";
}
