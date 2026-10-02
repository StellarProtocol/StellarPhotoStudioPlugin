using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using Il2CppInterop.Runtime;
using Panda.ZEffect;
using Panda.ZGame;
using Stellar.Abstractions.Domain;
using UnityEngine;
using UnityEngine.Playables;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>Run 9 measurements: the town/field pause sequence, the long network pause, and the samplers they use.</summary>
public sealed partial class FreeCamProbe
{
    private sealed record R9Ent(long Uuid, string Kind, Vector3 Vis, Vector3 Attr, string Skill);

    private void R9TimeState(string when)
    {
        Try($"R9 time state {when}", () =>
        {
            var brain = CameraManager.Instance?.Brain;
            Log($"R9 TIME {when}: timeScale={Time.timeScale:F3} fixedDeltaTime={Time.fixedDeltaTime:F4} maximumDeltaTime={Time.maximumDeltaTime:F3} " +
                $"captureDeltaTime={Time.captureDeltaTime:F4} maxParticleDt={Time.maximumParticleDeltaTime:F3} " +
                $"brain ignoreTimeScale={brain?.m_IgnoreTimeScale} update={brain?.m_UpdateMethod} blendUpdate={brain?.m_BlendUpdateMethod}");
        });
    }

    /// <summary>What a timeScale of 0 can NOT stop: unscaled particle systems / animators / directors in the scene.</summary>
    private void R9Census()
    {
        Try("R9 census particles", () =>
        {
            var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<ParticleSystem>());
            int n = 0, unscaled = 0, playing = 0;
            foreach (var o in arr)
            {
                var ps = o.TryCast<ParticleSystem>();
                if (ps == null) continue;
                n++;
                if (ps.main.useUnscaledTime) unscaled++;
                if (ps.isPlaying) playing++;
            }
            Log($"R9 CENSUS ParticleSystem active={n} playing={playing} useUnscaledTime={unscaled}");
        });
        Try("R9 census animators", () =>
        {
            var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Animator>());
            var modes = new Dictionary<string, int>();
            foreach (var o in arr)
            {
                var a = o.TryCast<Animator>();
                if (a == null) continue;
                var k = a.updateMode.ToString();
                modes[k] = modes.TryGetValue(k, out var c) ? c + 1 : 1;
            }
            Log($"R9 CENSUS Animator n={arr.Length} updateModes=[{string.Join(" ", modes.Select(kv => $"{kv.Key}={kv.Value}"))}]");
        });
        Try("R9 census directors", () =>
        {
            var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<PlayableDirector>());
            var modes = new Dictionary<string, int>();
            foreach (var o in arr)
            {
                var d = o.TryCast<PlayableDirector>();
                if (d == null) continue;
                var k = $"{d.timeUpdateMode}/{d.state}";
                modes[k] = modes.TryGetValue(k, out var c) ? c + 1 : 1;
            }
            Log($"R9 CENSUS PlayableDirector n={arr.Length} [{string.Join(" ", modes.Select(kv => $"{kv.Key}={kv.Value}"))}]");
        });
    }

    // ---- the pause sequence ----------------------------------------------------------------------------------------

    private IEnumerator R9PauseTest(string label, int epoch, float pauseSeconds)
    {
        if (!R9AcquireCam(label)) yield break;
        yield return Wait(1.5f);
        R9TimeState($"{label} cam held");
        var cam = MainCam();
        var self = LiveModel(SelfEntity());
        if (cam == null || self == null) { Log("R9 no camera / self"); yield break; }
        var other = R9NearestOnScreen(cam, 30f);
        var regions = new[]
        {
            RegionAround(cam, self.GetChestPosition()),
            other != null ? RegionAround(cam, other.Value.Pos) : new RectInt(Screen.width / 4, Screen.height / 4, Screen.width / 2, Screen.height / 2),
            new RectInt(Screen.width / 4, Screen.height / 4, Screen.width / 2, Screen.height / 2),
        };
        Log($"R9 regions self / other({(other == null ? "none: centre" : $"{other.Value.Kind}:{other.Value.Uuid}@{other.Value.Dist:F1}m")}) / centre");

        var u1 = CaptureMany($"R9_{label}_U1", regions);
        yield return Wait(0.6f);
        var u2 = CaptureMany($"R9_{label}_U2", regions);
        Log($"R9 {label} UNPAUSED baseline 0.6 s: {R9Diffs(u1, u2)}");

        R9ReleaseCam();
        yield return Wait(0.5f);
        yield return R9GameCamRotate($"{label} UNPAUSED baseline, free camera off");
        if (!R9AcquireCam($"{label} re-acquire for the pause")) yield break;
        yield return Wait(1.5f);
        yield return R9StartAction(label);
        var e0 = R9Ents(40f);
        var fx0 = R9Fx();
        R9Pause(label, pauseSeconds);
        var fw0 = _r9FwTicks; _r9Chat = 0; _r9Combat = 0; R9ResetCamCounters();
        var mark = SendMark();
        Log($"R9 LUA net at pause: {Lua(R9NetReadLua)}");

        yield return Wait(0.5f);
        var p1 = CaptureMany($"R9_{label}_P1", regions);
        var e1 = R9Ents(40f);
        var fx1 = R9Fx();
        Log($"R9 {label} self at +0.5s: skill={R9Skill(SelfEntity())} actorState={Lua("return tostring(Z.EntityMgr.PlayerEnt:GetLuaLocalAttrState())")}");
        yield return Wait(2f);
        var p2 = CaptureMany($"R9_{label}_P2", regions);
        var e2 = R9Ents(40f);
        var fx2 = R9Fx();
        Log($"R9 {label} PAUSED 2.0 s (P1->P2): {R9Diffs(p1, p2)}");
        Log($"R9 {label} PAUSED entities P1->P2: {R9EntDelta(e1, e2)}");
        Log($"R9 {label} effect clock: preP->P1 {R9FxDelta(fx0, fx1)} | P1->P2 {R9FxDelta(fx1, fx2)}");
        Log($"R9 {label} fwTicks during first 2.5 s paused: {_r9FwTicks - fw0} ; {R9CamCounters()}");

        yield return R9CameraMove(label, regions, p1, epoch);
        if (Aborted(epoch)) { R9Resume("scene changed"); yield break; }
        yield return R9FwCapture(label);
        yield return R9BlendTest(label);
        yield return R9HoldAndSample(label, epoch, pauseSeconds, fw0, mark, e0);

        R9Resume($"{label} done");
        yield return R9AfterResume(label, regions, e0);
        R9ReleaseCam();
    }

    /// <summary>Starts a self skill (the game's own skill-slot press: <c>PlayerInputController:Attack(slot, true/false)</c>,
    /// what <c>skill_slot_obj.keyFuncCall</c> does), a client-side world effect and, if no skill started, a wave emote.</summary>
    private IEnumerator R9StartAction(string label)
    {
        var skill = "0";
        foreach (var slot in new[] { 1, 2, 3, 4 })
        {
            Log($"R9 SKILL press slot {slot}: {Lua($"Z.PlayerInputController:Attack({slot}, true); return 'pressed'")}");
            yield return Wait(0.12f);
            Lua($"Z.PlayerInputController:Attack({slot}, false); return 'released'");
            yield return Wait(0.15f);
            skill = R9Skill(SelfEntity());
            Log($"R9 SKILL slot {slot} -> self skill={skill} actorState={Lua("return tostring(Z.EntityMgr.PlayerEnt:GetLuaLocalAttrState())")}");
            if (skill != "0" && !skill.StartsWith("ERR")) break;
        }
        Log($"R9 EFFECT {SpawnSelfEffect()}");
        if (skill == "0" || skill.StartsWith("ERR")) Log($"R9 EMOTE (no skill started) {PlayEmote(9011)}");
        yield return Wait(0.3f);
    }

    private IEnumerator R9CameraMove(string label, RectInt[] regions, Shot[]? p1, int epoch)
    {
        var c = _r9Cam;
        var self = LiveModel(SelfEntity());
        var cam = MainCam();
        if (c == null || self == null || cam == null) yield break;
        var center = self.GetChestPosition();
        var startPos = cam.transform.position;
        var startPose = R9LookPose(startPos, center, cam.fieldOfView);
        var e0 = cam.transform.rotation.eulerAngles;
        var exactPose = new CameraPose(new Position3D(startPos.x, startPos.y, startPos.z), e0.y, e0.x > 180f ? e0.x - 360f : e0.x, 0f, cam.fieldOfView);
        var startRot = cam.transform.rotation;
        var offset = startPos - center;
        float acc = 0f;
        R9ResetCamCounters();
        _r9CamDriver = dt =>
        {
            acc += dt;
            var a = Mathf.Min(acc / 3f, 1f) * 90f;
            return R9LookPose(center + Quaternion.Euler(0f, a, 0f) * offset, center, startPose.Fov);
        };
        var t0 = Time.realtimeSinceStartup;
        yield return Wait(1.6f);
        CaptureMany($"R9_{label}_P_orbit_mid", regions);
        yield return Wait(1.6f);
        Log($"R9 {label} CAMERA orbit while paused: {R9CamCounters()} realElapsed={Time.realtimeSinceStartup - t0:F2}s mainCam={CamPose(MainCam())} startPos={V(startPos)} moved={Vector3.Distance(MainCam()!.transform.position, startPos):F2}m");
        _r9CamDriver = _ => exactPose;
        yield return Wait(0.5f);
        var p3 = CaptureMany($"R9_{label}_P3_back", regions);
        Log($"R9 {label} CAMERA back to the P1 pose: camDist={Vector3.Distance(MainCam()!.transform.position, startPos):F3}m camAngle={Quaternion.Angle(MainCam()!.transform.rotation, startRot):F3}deg P1->P3 (3.7 s later, same pose) {R9Diffs(p1, p3)}");
        _r9CamDriver = null;
    }

    private IEnumerator R9FwCapture(string label)
    {
        var t0 = Time.realtimeSinceStartup;
        var task = _services.ScreenCapture.CaptureAsync(new CaptureRequest { Scale = 1, Directory = _outDir, FileStem = $"r9_{label}_fwcapture_paused" });
        while (!task.IsCompleted && Time.realtimeSinceStartup - t0 < 15f) yield return null;
        if (!task.IsCompleted) { Log($"R9 {label} FW CAPTURE did not complete within 15 s while paused (isCapturing={_services.ScreenCapture.IsCapturing})"); yield break; }
        var r = task.Exception == null ? task.Result : null;
        Log($"R9 {label} FW CAPTURE while paused: {(r == null ? $"faulted {task.Exception?.GetBaseException().Message}" : $"success={r.Success} {r.Width}x{r.Height} path={r.Path} err={r.Error}")} in {Time.realtimeSinceStartup - t0:F2}s");
    }

    /// <summary>Releasing the free camera while paused: does the brain's blend back to the game camera progress (the brain
    /// reads the SCALED delta unless <c>m_IgnoreTimeScale</c>)? Then the same with the flag on, and a re-acquire.</summary>
    private IEnumerator R9BlendTest(string label)
    {
        var brain = CameraManager.Instance?.Brain;
        if (brain == null) yield break;
        var prevIgnore = brain.m_IgnoreTimeScale;
        var prevBlend = brain.m_DefaultBlend;
        Arm("r9.brain", () =>
        {
            CinemachineCore.UniformDeltaTimeOverride = -1f;
            _r9UniformDt = false;
            var b = CameraManager.Instance?.Brain;
            if (b != null) { b.m_IgnoreTimeScale = prevIgnore; b.m_DefaultBlend = prevBlend; }
        });
        // (a) release while paused, brain as the game left it
        R9ReleaseCam();
        yield return R9SampleBlend($"{label} A release (as is) ignoreTimeScale={brain.m_IgnoreTimeScale} uniformDt={CinemachineCore.UniformDeltaTimeOverride:F2}", brain, 1.5f);
        // (b) m_IgnoreTimeScale = true
        brain.m_IgnoreTimeScale = true;
        yield return R9SampleBlend($"{label} B ignoreTimeScale=true", brain, 1.5f);
        // (c) CinemachineCore.UniformDeltaTimeOverride = unscaled dt every frame (set in R9Frame)
        _r9UniformDt = true;
        yield return R9SampleBlend($"{label} C uniformDeltaTimeOverride=unscaled", brain, 1.5f);
        _r9UniformDt = false;
        CinemachineCore.UniformDeltaTimeOverride = -1f;
        brain.m_IgnoreTimeScale = prevIgnore;
        // (d) game camera input while paused, free camera off: the game's rotation input (PlayerInputController.Rotation)
        yield return R9GameCamRotate($"{label} paused, free camera off");
        // (e) re-acquire with a Cut default blend: does the main camera follow our pose at once?
        var cut = brain.m_DefaultBlend;
        cut.m_Style = CinemachineBlendDefinition.Style.Cut;
        brain.m_DefaultBlend = cut;
        if (R9AcquireCam($"{label} re-acquire (Cut blend)"))
        {
            yield return R9SampleBlend($"{label} E re-acquire with Cut", brain, 0.5f);
            yield return R9FollowCheck($"{label} E after Cut re-acquire");
        }
        brain.m_DefaultBlend = prevBlend;
        // (f) release + re-acquire with the game blend but the uniform override on
        R9ReleaseCam();
        _r9UniformDt = true;
        yield return R9SampleBlend($"{label} F release with uniformDt", brain, 1f);
        if (R9AcquireCam($"{label} re-acquire (uniformDt)"))
        {
            yield return R9SampleBlend($"{label} F re-acquire with uniformDt", brain, 1f);
            yield return R9FollowCheck($"{label} F after uniformDt re-acquire");
        }
        _r9UniformDt = false;
        Release("r9.brain");
        Log($"R9 {label} brain restored ignoreTimeScale={brain.m_IgnoreTimeScale} blend={brain.m_DefaultBlend.m_Style}/{brain.m_DefaultBlend.m_Time:F2} uniformDt={CinemachineCore.UniformDeltaTimeOverride:F2}");
    }

    private bool _r9UniformDt;

    /// <summary>Drives the free camera 1 m up for 0.5 s: does Main Camera follow?</summary>
    private IEnumerator R9FollowCheck(string what)
    {
        var cam = MainCam();
        if (cam == null || _r9Cam == null) yield break;
        var p0 = cam.transform.position;
        var e = cam.transform.rotation.eulerAngles;
        var target = new CameraPose(new Position3D(p0.x, p0.y + 1f, p0.z), e.y, e.x > 180f ? e.x - 360f : e.x, 0f, cam.fieldOfView);
        _r9CamDriver = _ => target;
        yield return Wait(0.5f);
        Log($"R9 FOLLOW {what}: main camera moved {Vector3.Distance(MainCam()!.transform.position, p0):F3}m toward a +1.000 m pose");
        _r9CamDriver = null;
    }

    /// <summary>Feeds the game's own camera-rotation input (<c>PlayerInputController:Rotation</c>, the joystick/mouse path) for
    /// 1 s and reports how far Main Camera turned.</summary>
    private IEnumerator R9GameCamRotate(string what)
    {
        var cam = MainCam();
        if (cam == null) yield break;
        var r0 = cam.transform.rotation;
        var t0 = Time.realtimeSinceStartup;
        var res = "";
        while (Time.realtimeSinceStartup - t0 < 1f)
        {
            res = Lua("Z.PlayerInputController:Rotation(Vector2.New(1, 0)); return 'ok'");
            yield return null;
        }
        Lua("Z.PlayerInputController:Rotation(Vector2.New(0, 0)); return 'ok'");
        yield return Wait(0.3f);
        Log($"R9 GAMECAM rotate {what}: turned {Quaternion.Angle(r0, MainCam()!.transform.rotation):F2}deg in 1 s (lua {res})");
    }

    private IEnumerator R9SampleBlend(string what, CinemachineBrain brain, float seconds)
    {
        var t0 = Time.realtimeSinceStartup;
        var parts = new List<string>();
        var startPos = MainCam()?.transform.position ?? Vector3.zero;
        var f = 0;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            if (f++ % 15 == 0)
            {
                var b = brain.ActiveBlend;
                parts.Add($"t={Time.realtimeSinceStartup - t0:F2}:{(b == null ? "none" : $"{b.TimeInBlend:F2}/{b.Duration:F2}")}");
            }
            yield return null;
        }
        Log($"R9 BLEND {what}: active={VcamName(brain.ActiveVirtualCamera)} isBlending={brain.IsBlending} camMoved={Vector3.Distance(MainCam()?.transform.position ?? Vector3.zero, startPos):F3}m [{string.Join(" ", parts)}]");
    }

    private IEnumerator R9HoldAndSample(string label, int epoch, float pauseSeconds, int fw0, long mark, Dictionary<long, R9Ent> e0)
    {
        var next = 0f;
        var lastFw = _r9FwTicks;
        while (_r9Paused && Time.realtimeSinceStartup - _r9PausedAt < pauseSeconds)
        {
            var el = Time.realtimeSinceStartup - _r9PausedAt;
            if (el >= next)
            {
                next = el + 10f;
                var e = R9Ents(40f);
                Log($"R9 {label} HOLD +{el:F0}s phase={_services.ClientState.Phase} fwTicks+{_r9FwTicks - lastFw} chat={_r9Chat} combatEvents={_r9Combat} " +
                    $"pauseFrames={_r9Frames} dt>0={_r9DtPositive} gameWrites={_r9WritesAtUpdate}/{_r9WritesAtLate} sends {SendsSince(mark)} | net {Lua(R9NetReadLua)} | since pause {R9EntDelta(e0, e)}");
                lastFw = _r9FwTicks;
            }
            if (Aborted(epoch)) { Log($"R9 {label} ABORT: scene/phase changed while paused phase={_services.ClientState.Phase}"); yield break; }
            yield return Wait(0.25f);
        }
        Log($"R9 {label} HOLD done: fwTicks during pause={_r9FwTicks - fw0} {R9CamCounters()}");
    }

    private IEnumerator R9AfterResume(string label, RectInt[] regions, Dictionary<long, R9Ent> e0)
    {
        var fw = _r9FwTicks;
        yield return Frames(3);
        Log($"R9 {label} RESUME +3f gaps {R9Gaps()} self skill={R9Skill(SelfEntity())}");
        yield return Wait(1f);
        Log($"R9 {label} RESUME +1s gaps {R9Gaps()} self skill={R9Skill(SelfEntity())} fwTicks+{_r9FwTicks - fw}");
        var r1 = CaptureMany($"R9_{label}_R1", regions);
        yield return Wait(0.6f);
        var r2 = CaptureMany($"R9_{label}_R2", regions);
        Log($"R9 {label} RESUMED 0.6 s (motion back?): {R9Diffs(r1, r2)}");
        yield return Wait(1.4f);
        Log($"R9 {label} RESUME +3s gaps {R9Gaps()} self skill={R9Skill(SelfEntity())} actorState={Lua("return tostring(Z.EntityMgr.PlayerEnt:GetLuaLocalAttrState())")} net {Lua(R9NetReadLua)}");
        yield return Wait(3f);
        Log($"R9 {label} RESUME +6s gaps {R9Gaps()} self skill={R9Skill(SelfEntity())} phase={_services.ClientState.Phase} chatSinceResume+pause={_r9Chat} combatEvents={_r9Combat} {R9TimeLine()}");
    }

    private IEnumerator R9LongPause(int epoch, float seconds)
    {
        var e0 = R9Ents(40f);
        Log($"R9 LONG net before: {Lua(R9NetReadLua)}");
        R9Pause("long", seconds);
        var fw0 = _r9FwTicks; _r9Chat = 0; _r9Combat = 0;
        var mark = SendMark();
        var next = 0f;
        while (_r9Paused && Time.realtimeSinceStartup - _r9PausedAt < seconds)
        {
            var el = Time.realtimeSinceStartup - _r9PausedAt;
            if (el >= next)
            {
                next = el + 15f;
                Log($"R9 long HOLD +{el:F0}s phase={_services.ClientState.Phase} fwTicks={_r9FwTicks - fw0} chat={_r9Chat} combatEvents={_r9Combat} " +
                    $"pauseFrames={_r9Frames} dt>0={_r9DtPositive} gameWrites={_r9WritesAtUpdate}/{_r9WritesAtLate} sends {SendsSince(mark)} | net {Lua(R9NetReadLua)} | since pause {R9EntDelta(e0, R9Ents(40f))}");
            }
            if (Aborted(epoch)) { Log($"R9 long ABORT: scene/phase changed while paused phase={_services.ClientState.Phase}"); break; }
            yield return Wait(0.5f);
        }
        R9Resume("long done");
        yield return Wait(1f);
        Log($"R9 long RESUME +1s gaps {R9Gaps()} net {Lua(R9NetReadLua)}");
        yield return Wait(5f);
        Log($"R9 long RESUME +6s gaps {R9Gaps()} phase={_services.ClientState.Phase} census {R7Census()} net {Lua(R9NetReadLua)} {R9TimeLine()}");
    }

    // ---- samplers -------------------------------------------------------------------------------------------------

    private string R9TimeLine() => $"timeScale={Time.timeScale:F3} time={Time.time:F1} realtime={Time.realtimeSinceStartup:F1}";

    private static string R9Skill(ZEntity? e) => e == null ? "none" : Safe(() => EntityAttrExtensions.GetSkillId(e).ToString());

    private (long Uuid, string Kind, Vector3 Pos, float Dist)? R9NearestOnScreen(Camera cam, float range)
    {
        foreach (var x in AllEntities(range).Where(x => x.Kind is "CharEnt" or "NpcEnt" or "MonsterEnt" or "PetEnt"))
        {
            var m = LiveModel(EntByUuid(x.Uuid));
            if (m == null) continue;
            var p = m.GetChestPosition();
            var vp = cam.WorldToViewportPoint(p);
            if (vp.z > 0f && vp.x > 0.1f && vp.x < 0.9f && vp.y > 0.1f && vp.y < 0.9f) return (x.Uuid, x.Kind, p, x.Dist);
        }
        return null;
    }

    private Dictionary<long, R9Ent> R9Ents(float range)
    {
        var d = new Dictionary<long, R9Ent>();
        foreach (var x in AllEntities(range).Where(x => x.Kind is "CharEnt" or "NpcEnt" or "MonsterEnt" or "PetEnt" or "VehicleEnt").Take(20))
        {
            var e = EntByUuid(x.Uuid);
            var m = LiveModel(e);
            if (m == null) continue;
            try { d[x.Uuid] = new R9Ent(x.Uuid, x.Kind, m.ModelGoComp?.Position ?? Vector3.zero, m.GetAttrGoPosition(), R9Skill(e)); } catch { }
        }
        var s = SelfEntity();
        var sm = LiveModel(s);
        if (s != null && sm != null) try { d[s.Uuid] = new R9Ent(s.Uuid, "SELF", sm.ModelGoComp?.Position ?? Vector3.zero, sm.GetAttrGoPosition(), R9Skill(s)); } catch { }
        return d;
    }

    /// <summary>Per entity present in both: drawn (vis) and logical (attr) movement; summary = max + movers.</summary>
    private static string R9EntDelta(Dictionary<long, R9Ent> a, Dictionary<long, R9Ent> b)
    {
        var rows = a.Keys.Where(b.ContainsKey).Select(k => (a[k], b[k])).ToList();
        if (rows.Count == 0) return "no common entities";
        var dv = rows.Select(r => (r.Item1, V: Vector3.Distance(r.Item1.Vis, r.Item2.Vis), A: Vector3.Distance(r.Item1.Attr, r.Item2.Attr), S: r.Item2.Skill)).ToList();
        var visMovers = dv.Count(x => x.V > 0.02f);
        var attrMovers = dv.Count(x => x.A > 0.02f);
        var top = dv.OrderByDescending(x => Math.Max(x.V, x.A)).Take(5).Select(x => $"{x.Item1.Kind}:{x.Item1.Uuid}(vis{x.V:F2}/attr{x.A:F2}/sk{x.S})");
        return $"n={rows.Count} visMoved>2cm={visMovers} maxVis={dv.Max(x => x.V):F3}m attrMoved>2cm={attrMovers} maxAttr={dv.Max(x => x.A):F2}m [{string.Join(" ", top)}]";
    }

    /// <summary>|drawn - logical| per entity now (what a resume has to close).</summary>
    private string R9Gaps()
    {
        var e = R9Ents(40f);
        if (e.Count == 0) return "none";
        var g = e.Values.Select(x => (x, G: Vector3.Distance(x.Vis, x.Attr))).OrderByDescending(x => x.G).ToList();
        return $"n={g.Count} gap>0.5m={g.Count(x => x.G > 0.5f)} gap>2m={g.Count(x => x.G > 2f)} max={g[0].G:F2}m [{string.Join(" ", g.Take(4).Select(x => $"{x.x.Kind}:{x.G:F2}"))}]";
    }

    private Dictionary<long, float> R9Fx()
    {
        var d = new Dictionary<long, float>();
        var em = ZEffectManager.IsCreated ? ZEffectManager.Instance : null;
        if (em == null) return d;
        foreach (var uid in EffectKeys(em, out _))
        {
            var s = ParticleState(em, uid);
            if (s.Systems > 0) d[uid] = s.Time;
        }
        return d;
    }

    private static string R9FxDelta(Dictionary<long, float> a, Dictionary<long, float> b)
    {
        var common = a.Keys.Where(b.ContainsKey).ToList();
        var adv = common.Count(k => Math.Abs(b[k] - a[k]) > 0.001f);
        var sum = common.Sum(k => b[k] - a[k]);
        return $"effects={b.Count} common={common.Count} advancing={adv} sumParticleTimeDelta={sum:F3}s new={b.Keys.Count(k => !a.ContainsKey(k))}";
    }

    private string R9Diffs(Shot[]? a, Shot[]? b)
    {
        if (a == null || b == null) return "capture missing";
        var names = new[] { "self", "other", "centre" };
        return string.Join(" | ", Enumerable.Range(0, Math.Min(a.Length, b.Length)).Select(i => $"{names[i]} {DiffText(a[i], b[i])}"));
    }
}
