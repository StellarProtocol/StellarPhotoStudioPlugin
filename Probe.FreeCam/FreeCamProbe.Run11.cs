using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Panda.ZGame;
using Stellar.Abstractions.Domain;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 11 (2026-10-02, owner report on the TEST window during run 10): while the framework time pause holds, (1) a MOVING
/// remote player still animates its movement in place, and (2) the local character given movement input plays the run
/// animation in place. Measures, per frame: the remote mover's drawn / logical position and its ECS animation controller's
/// managed base state (stateID, normalizedTime, time, speed, fade, parameters) — which changes, and how often — plus
/// in-process region captures (pixel diffs), the ECS worlds' time data, and outgoing sends. Then the same for the local
/// player driven through the game's own input entry (<c>PlayerInputController:Move</c>, free camera off). Probe-only.
/// </summary>
public sealed partial class FreeCamProbe
{
    private IDisposable? _r11Token;

    private static readonly Dictionary<uint, (int Layer, int State, float LastSpeed)> s_r11Ecs = new();
    private static bool s_r11Count;

    private static void R11LayerPrefix(uint __0, int __1, float __2)
    {
        if (!s_r11Count) return;
        s_r11Ecs.TryGetValue(__0, out var c);
        s_r11Ecs[__0] = (c.Layer + 1, c.State, __2);
    }

    private static readonly Dictionary<uint, int[]> s_r11Events = new();

    // ECSAnimController.onEventCallback(int layer, uint stateID, ECSModel.EventType type): the ECS animator's own events
    // (enter / exit / finished / LOOP END) — a loop end can only come from an animation whose time advances.
    private static void R11EventPrefix(Panda.ZGame.ECSAnimController __instance, int __0, ECSModel.EventType __2)
    {
        if (!s_r11Count) return;
        uint uid;
        try { uid = __instance.ecsUID_; } catch { return; }
        if (!s_r11Events.TryGetValue(uid, out var c)) s_r11Events[uid] = c = new int[5 * 3];
        var i = (int)__2;
        if (i is >= 0 and < 5 && __0 is >= 0 and < 3) c[__0 * 5 + i]++;
    }

    private static string R11EventText(uint uid)
    {
        if (!s_r11Events.TryGetValue(uid, out var c)) return "none";
        var names = new[] { "enter", "exit", "fin", "loopEnd", "rel" };
        var parts = new List<string>();
        for (var l = 0; l < 3; l++)
            for (var i = 0; i < 5; i++)
                if (c[l * 5 + i] > 0) parts.Add($"L{l}.{names[i]}={c[l * 5 + i]}");
        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }

    /// <summary>Every entity's ECS animation events over <paramref name="seconds"/> (no mover needed): per kind, how many
    /// entities had a loop end / state enter, and the top ones, with whether their logical position moved.</summary>
    private IEnumerator R11EventCensus(string label, float seconds)
    {
        var ents = AllEntities(80f);
        var uidOf = new Dictionary<uint, (long Uuid, string Kind, Vector3 Attr)>();
        foreach (var x in ents)
        {
            var m = LiveModel(EntByUuid(x.Uuid));
            if (m == null) continue;
            var u = R11Uid(m);
            if (u != 0) uidOf[u] = (x.Uuid, x.Kind, m.GetAttrGoPosition());
        }
        if (SelfEntity() is { } se && LiveModel(se) is { } sm && R11Uid(sm) is var su && su != 0) uidOf[su] = (se.Uuid, "SELF", sm.GetAttrGoPosition());
        s_r11Events.Clear();
        s_r11Ecs.Clear();
        s_r11Calls.Clear();
        s_r11Count = true;
        yield return Wait(seconds);
        s_r11Count = false;
        var rows = new List<string>();
        var byKind = new Dictionary<string, (int N, int Loop, int Enter, int Moved, int Writes)>();
        foreach (var kv in uidOf)
        {
            var (uuid, kind, a0) = kv.Value;
            var m = LiveModel(EntByUuid(uuid));
            var moved = m != null && Vector3.Distance(a0, m.GetAttrGoPosition()) > 0.3f;
            s_r11Events.TryGetValue(kv.Key, out var c);
            s_r11Ecs.TryGetValue(kv.Key, out var w);
            var loop = c == null ? 0 : c[3] + c[8] + c[13];
            var enter = c == null ? 0 : c[0] + c[5] + c[10];
            byKind.TryGetValue(kind, out var k);
            byKind[kind] = (k.N + 1, k.Loop + (loop > 0 ? 1 : 0), k.Enter + (enter > 0 ? 1 : 0), k.Moved + (moved ? 1 : 0), k.Writes + (w.Layer + w.State > 0 ? 1 : 0));
            if (loop + enter + w.Layer + w.State > 0 || s_r11Calls.ContainsKey(kv.Key))
                if (rows.Count < 15) rows.Add($"{kind}:{uuid} moved={moved} ev[{R11EventText(kv.Key)}] layerData={w.Layer} stateData={w.State} calls[{R11CallText(kv.Key)}]");
        }
        var unknown = s_r11Events.Keys.Count(k => !uidOf.ContainsKey(k));
        Log($"R11 EVENT-CENSUS {label} {seconds:F0}s timeScale={Time.timeScale:F3} entities={uidOf.Count} uidsWithEventsNotMapped={unknown} perKind[" +
            string.Join(" ", byKind.Select(kv => $"{kv.Key}: n={kv.Value.N} loopEnd={kv.Value.Loop} enter={kv.Value.Enter} logicalMoved={kv.Value.Moved} ecsWrites={kv.Value.Writes}")) + "]");
        Log($"R11 EVENT-CENSUS {label} rows {string.Join(" | ", rows)}");
    }

    // per-uid counts of the managed controller's request entry points (index = R11Methods position)
    private static readonly string[] R11Methods = { "PlayBaseState", "playBaseState", "PlayUpperState", "playUpperState",
        "PlayAdditiveState", "PlayManualClip", "playManualClip", "set_Speed", "SetPersistTime", "MarkForceReplayState", "SetDefaultState" };
    private static readonly Dictionary<uint, int[]> s_r11Calls = new();
    private static readonly Dictionary<System.Reflection.MethodBase, int> s_r11MethodIndex = new();

    private static void R11CallPrefix(Panda.ZGame.ECSAnimController __instance, System.Reflection.MethodBase __originalMethod)
    {
        if (!s_r11Count) return;
        uint uid;
        try { uid = __instance.ecsUID_; } catch { return; }
        if (!s_r11MethodIndex.TryGetValue(__originalMethod, out var i)) return;
        if (!s_r11Calls.TryGetValue(uid, out var c)) s_r11Calls[uid] = c = new int[R11Methods.Length];
        c[i]++;
    }

    private static string R11CallText(uint uid)
    {
        if (!s_r11Calls.TryGetValue(uid, out var c)) return "none";
        var parts = new List<string>();
        for (var i = 0; i < c.Length; i++) if (c[i] > 0) parts.Add($"{R11Methods[i]}={c[i]}");
        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }

    private void R11HookCalls()
    {
        var pre = new HarmonyLib.HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(R11CallPrefix), BindingFlags.NonPublic | BindingFlags.Static));
        var t = typeof(Panda.ZGame.ECSAnimController);
        for (var i = 0; i < R11Methods.Length; i++)
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == R11Methods[i]))
            {
                try { _harmony!.Patch(m, prefix: pre); s_r11MethodIndex[m] = i; }
                catch (Exception ex) { Log($"R11 hook {m.Name} FAILED {ex.Message}"); }
            }
        }
        Log($"R11 HOOKS controller entry points patched: {s_r11MethodIndex.Count}");
    }

    private static void R11StatePrefix(uint __0)
    {
        if (!s_r11Count) return;
        s_r11Ecs.TryGetValue(__0, out var c);
        s_r11Ecs[__0] = (c.Layer, c.State + 1, c.LastSpeed);
    }

    private void R11Hook()
    {
        try
        {
            _harmony ??= _services.Harmony.Create("freecamprobe");
            var t = typeof(ECSModel.ECSModelResourceManager);
            var layer = t.GetMethod("SetAnimatorLayerData", BindingFlags.Public | BindingFlags.Static);
            var state = t.GetMethod("SetAnimatorStateData", BindingFlags.Public | BindingFlags.Static);
            _harmony.Patch(layer, prefix: new HarmonyLib.HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(R11LayerPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
            _harmony.Patch(state, prefix: new HarmonyLib.HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(R11StatePrefix), BindingFlags.NonPublic | BindingFlags.Static)));
            var ev = typeof(Panda.ZGame.ECSAnimController).GetMethod("onEventCallback", BindingFlags.Public | BindingFlags.Instance);
            _harmony.Patch(ev, prefix: new HarmonyLib.HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(R11EventPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
            Log("R11 HOOKS SetAnimatorLayerData + SetAnimatorStateData + ECSAnimController.onEventCallback counting prefixes on");
            R11HookCalls();
        }
        catch (Exception ex) { Log($"R11 HOOKS FAILED {ex.GetType().Name}: {ex.Message}"); }
    }

    private static uint R11Uid(ZModel m)
    {
        try { return m.AnimComp?.TryCast<ECSAnimComp>()?.controller_?.ecsUID_ ?? 0u; } catch { return 0u; }
    }

    private IEnumerator StepSetup11()
    {
        R11Hook();
        Log($"R11 self={_selfUuid} census {R7Census()}");
        Log($"R11 ECS worlds {R11Worlds()}");
        yield break;
    }

    private bool _r11InField;
    private string _r11Input = "move";

    private IEnumerator StepRemote11()
    {
        var epoch = _sceneEpoch;
        // the local input path must move the character with the clock RUNNING, or the paused test proves nothing
        Log($"R11 LOCAL focus: {Lua("Z.PlayerInputController:OnApplicationFocus(true); return 'ok'")}");
        var moved = 0;
        yield return R11Drive("unpaused-local-move", "move", 1.5f, n => moved = n);
        if (moved == 0) { yield return R11Drive("unpaused-local-automove", "auto", 2f, n => moved = n); if (moved > 0) _r11Input = "auto"; }
        Log($"R11 LOCAL input path = {_r11Input} (moved unpaused: {moved > 0})");
        var mover = 0L;
        yield return R11FindMover(30f, u => mover = u);
        if ((mover == 0 && Environment.GetEnvironmentVariable("STELLAR_R11_FIELD") == "1") || Environment.GetEnvironmentVariable("STELLAR_R11_FIELD") == "force")
        {
            yield return R9GoField();
            var ep0 = _sceneEpoch; var tw = Time.realtimeSinceStartup;
            while (ep0 == _sceneEpoch && Time.realtimeSinceStartup - tw < 20f) yield return Wait(0.5f);
            yield return Wait(3f);
            _r11InField = true;
            epoch = _sceneEpoch;
            Log($"R11 field census {R7Census()}");
            yield return R11FindMover(20f, u => mover = u);
        }
        if (mover != 0) { yield return R11Watch("unpaused-remote", mover, 2f); yield return R11Timeline("unpaused-remote", mover, 3f); }
        Log($"R11 input before freeze [{R11Input()}]");
        yield return R11EventCensus("unpaused", 8f);
        _r11Token = _services.SceneFreeze.Freeze();
        Arm("r11.freeze", () => { _r11Token?.Dispose(); _r11Token = null; });
        Log($"R11 FREEZE on: fwFrozen={_services.SceneFreeze.IsFrozen} timeScale={Time.timeScale:F3} holds={_services.SceneFreeze.HoldsPositions} input[{R11Input()}]");
        yield return Wait(1f);
        yield return R11EventCensus("paused", 20f);
        var paused = 0L;
        yield return R11FindMover(15f, u => paused = u);
        if (paused == 0) Log("R11 REMOTE no entity moving (logical) while paused within 20 s");
        else { yield return R11Watch("paused-remote", paused, 4f); yield return R11Timeline("paused-remote", paused, 5f); }
        yield return R11Timeline("paused-self-idle", _selfUuid, 3f);
        if (Aborted(epoch)) yield break;
        yield return R11Drive("paused-local", _r11Input, 3f, _ => { });
        var drivingT = true;
        ProbeTicks.LateTick = () => { if (drivingT) Lua("Z.PlayerInputController:Move(Vector2.New(0, 1)); return 'ok'"); };
        yield return R11Timeline("paused-self-move", _selfUuid, 3f);
        drivingT = false;
        ProbeTicks.LateTick = null;
        Lua("Z.PlayerInputController:Move(Vector2.New(0, 0)); return 'ok'");
        yield return R11Watch("paused-local-after", _selfUuid, 1.5f);
        Release("r11.freeze");
        Log($"R11 FREEZE off: timeScale={Time.timeScale:F3} input[{R11Input()}]");
        yield return Wait(1f);
        if (_r11InField) yield return R8GoHome("r11 return");
    }

    /// <summary>Drives the local player through the game's own input entry for <paramref name="seconds"/> while R11Watch
    /// samples; reports the logical steps seen.</summary>
    private IEnumerator R11Drive(string label, string how, float seconds, Action<int> attrSteps)
    {
        var self = SelfEntity();
        var m = LiveModel(self);
        if (self == null || m == null) yield break;
        var driving = true;
        var pumped = 0;
        var lua = "";
        if (how == "auto")
        {
            var p = m.GetAttrGoPosition();
            var fwd = m.ModelGoComp != null ? m.ModelGoComp.Rotation * Vector3.forward : Vector3.forward;
            var to = p + fwd * 6f;
            lua = Lua($"Z.PlayerInputController:AutoMove(true, Vector3.New({to.x:F2},{to.y:F2},{to.z:F2})); return 'ok'");
        }
        else ProbeTicks.LateTick = () => { if (driving) { lua = Lua("Z.PlayerInputController:Move(Vector2.New(0, 1)); return 'ok'"); pumped++; } };
        var a0 = m.GetAttrGoPosition();
        yield return R11Watch(label, self.Uuid, seconds);
        driving = false;
        ProbeTicks.LateTick = null;
        if (how == "auto") Lua("Z.PlayerInputController:AutoMove(false); return 'ok'");
        else Lua("Z.PlayerInputController:Move(Vector2.New(0, 0)); return 'ok'");
        var a1 = LiveModel(SelfEntity())?.GetAttrGoPosition() ?? a0;
        Log($"R11 {label} input={how} pumped={pumped} lua={lua} selfLogicalMoved={Vector3.Distance(a0, a1):F2}m");
        attrSteps(Vector3.Distance(a0, a1) > 0.2f ? 1 : 0);
    }

    private IEnumerator R11FindMover(float seconds, Action<long> found)
    {
        var t0 = Time.realtimeSinceStartup;
        var last = new Dictionary<long, Vector3>();
        long other = 0;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            foreach (var x in AllEntities(50f).Where(x => x.Kind is "CharEnt" or "MonsterEnt" or "NpcEnt"))
            {
                var m = LiveModel(EntByUuid(x.Uuid));
                if (m == null) continue;
                var attr = m.GetAttrGoPosition();
                if (last.TryGetValue(x.Uuid, out var p) && Vector3.Distance(p, attr) > 0.4f && x.Dist < 45f)
                {
                    if (x.Kind == "CharEnt")
                    {
                        Log($"R11 MOVER {x.Kind}:{x.Uuid} attr moved {Vector3.Distance(p, attr):F2}m in 0.5 s dist={x.Dist:F1}m");
                        found(x.Uuid);
                        yield break;
                    }
                    if (other == 0) { other = x.Uuid; Log($"R11 mover candidate {x.Kind}:{x.Uuid} dist={x.Dist:F1}m"); }
                }
                last[x.Uuid] = attr;
            }
            yield return Wait(0.5f);
        }
        if (other != 0) { Log($"R11 MOVER (no player moved; fallback) {other}"); found(other); }
    }

    /// <summary>Per-frame samples of one entity's positions and animation controller for <paramref name="seconds"/>, plus a
    /// region capture every 0.5 s (diffed in sequence).</summary>
    private IEnumerator R11Watch(string label, long uuid, float seconds)
    {
        var shots = new List<Shot?>();
        var states = new List<string>();
        int frames = 0, stateChanges = 0, timeChanges = 0, drawnSteps = 0, attrSteps = 0;
        string prevState = "";
        float prevTime = float.NaN;
        Vector3? prevDrawn = null, prevAttr = null;
        var t0 = Time.realtimeSinceStartup;
        var nextShot = t0;
        RectInt? region = null;
        var sendMark = SendMark();
        var uid = R11Uid(LiveModel(EntByUuid(uuid))!);
        s_r11Ecs.Remove(uid);
        s_r11Events.Remove(uid);
        s_r11Count = true;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            var e = EntByUuid(uuid);
            var m = LiveModel(e);
            if (m == null) { Log($"R11 {label} {uuid} gone"); break; }
            frames++;
            var drawn = m.ModelGoComp?.Position ?? Vector3.zero;
            var attr = m.GetAttrGoPosition();
            if (prevDrawn is { } pd && Vector3.Distance(pd, drawn) > 0.005f) drawnSteps++;
            if (prevAttr is { } pa && Vector3.Distance(pa, attr) > 0.005f) attrSteps++;
            prevDrawn = drawn;
            prevAttr = attr;
            var (st, time) = R11State(m);
            if (st != prevState) { stateChanges++; if (states.Count < 12) states.Add($"f{frames}:{st}"); prevState = st; }
            if (!float.IsNaN(prevTime) && Math.Abs(time - prevTime) > 1e-5f) timeChanges++;
            prevTime = time;
            if (Time.realtimeSinceStartup >= nextShot)
            {
                var cam = MainCam();
                if (region == null && cam != null) region = RegionAround(cam, m.GetChestPosition(), 0.06f, 0.14f);
                shots.Add(region is { } rg ? Capture($"R11_{label}_{shots.Count}", rg) : null);
                nextShot += seconds / 6f;
            }
            yield return null;
        }
        s_r11Count = false;
        s_r11Ecs.TryGetValue(uid, out var ecs);
        Log($"R11 {label} {uuid} ecsUid={uid} ECS writes in {seconds:F1}s: SetAnimatorLayerData={ecs.Layer} (last speed {ecs.LastSpeed:F2}) SetAnimatorStateData={ecs.State} animEvents[{R11EventText(uid)}]");
        var diffs = new List<string>();
        for (var i = 1; i < shots.Count; i++) diffs.Add($"{Diff(shots[i - 1], shots[i]).ChangedPct:F1}%");
        Log($"R11 {label} {uuid} frames={frames} timeScale={Time.timeScale:F3} drawnSteps={drawnSteps} attrSteps={attrSteps} " +
            $"baseStateChanges={stateChanges} baseTimeChanges={timeChanges} regionDiffs=[{string.Join(" ", diffs)}] sends[{SendsSince(sendMark)}]");
        Log($"R11 {label} {uuid} states {string.Join(" | ", states)}");
    }

    /// <summary>A small render (640x360) of the main camera; returns the pixels of <paramref name="region"/> (in that
    /// scale) — cheap enough for a 10 Hz timeline.</summary>
    private Color32[]? R11Fast(RectInt region, int layerMask = -1)
    {
        var cam = MainCam();
        if (cam == null) return null;
        var rt = RenderTexture.GetTemporary(640, 360, 24);
        var prevMask = cam.cullingMask;
        var prevClear = cam.clearFlags;
        var prevBg = cam.backgroundColor;
        if (layerMask != -1) { cam.cullingMask = layerMask; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; }
        var prevT = cam.targetTexture;
        var prevA = RenderTexture.active;
        Texture2D? tex = null;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(region.width, region.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(region.x, region.y, region.width, region.height), 0, 0);
            tex.Apply();
            return tex.GetPixels32().ToArray();
        }
        catch { return null; }
        finally
        {
            cam.cullingMask = prevMask;
            cam.clearFlags = prevClear;
            cam.backgroundColor = prevBg;
            cam.targetTexture = prevT;
            RenderTexture.active = prevA;
            RenderTexture.ReleaseTemporary(rt);
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    private static float R11Pct(Color32[]? a, Color32[]? b)
    {
        if (a == null || b == null || a.Length != b.Length || a.Length == 0) return -1f;
        var n = 0;
        for (var i = 0; i < a.Length; i++)
            if (Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b))) > 12) n++;
        return 100f * n / a.Length;
    }

    /// <summary>A 10 Hz timeline of one entity: the region diff of its body (small render) against the previous sample and
    /// against a same-size background region beside it, with a mark when its managed anim state changed in between.</summary>
    private IEnumerator R11Timeline(string label, long uuid, float seconds)
    {
        var m0 = LiveModel(EntByUuid(uuid));
        var cam = MainCam();
        if (m0 == null || cam == null) yield break;
        var vp = cam.WorldToViewportPoint(m0.GetChestPosition());
        int w = 44, h = 70;
        int cx = Mathf.Clamp((int)(vp.x * 640) - w / 2, 0, 640 - w - 1), cy = Mathf.Clamp((int)(vp.y * 360) - h / 2, 0, 360 - h - 1);
        var body = new RectInt(cx, cy, w, h);
        var layer = -1;
        try { layer = m0.ModelGoComp?.Layer ?? -1; } catch { }
        var only = layer is >= 0 and < 32 ? 1 << layer : -1;
        var bgx = cx + w + 10 < 640 - w ? cx + w + 10 : Math.Max(0, cx - w - 10);
        var bg = new RectInt(bgx, cy, w, h);
        Color32[]? pb = null, pg = null;
        string prevState = "";
        var rows = new List<string>();
        float sumBody = 0f, sumBg = 0f;
        int n = 0, changedSamples = 0, quietSamples = 0;
        float quietBody = 0f;
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            var m = LiveModel(EntByUuid(uuid));
            if (m == null) break;
            var (st, _) = R11State(m);
            var stateChanged = prevState != "" && st != prevState;
            prevState = st;
            var cb = R11Fast(body, only);   // the character's own layer only: no grass, water or sky
            var cg = R11Fast(bg);
            if (pb != null)
            {
                var db = R11Pct(pb, cb);
                var dg = R11Pct(pg, cg);
                sumBody += db; sumBg += dg; n++;
                if (stateChanged) changedSamples++; else { quietSamples++; quietBody += db; }
                if (rows.Count < 40) rows.Add($"{db:F1}/{dg:F1}{(stateChanged ? "*" : "")}");
            }
            pb = cb;
            pg = cg;
            var until = Time.realtimeSinceStartup + 0.1f;
            while (Time.realtimeSinceStartup < until) yield return null;
        }
        Log($"R11 TIMELINE {label} {uuid} layer={layer} timeScale={Time.timeScale:F3} samples={n} meanBody={(n > 0 ? sumBody / n : -1):F2}% meanBackground={(n > 0 ? sumBg / n : -1):F2}% " +
            $"bodyLit={(pb == null ? -1 : 100f * pb.Count(c => c.r + c.g + c.b > 30) / Math.Max(1, pb.Length)):F0}% meanBodyWithoutStateChange={(quietSamples > 0 ? quietBody / quietSamples : -1):F2}% stateChangeSamples={changedSamples} body/bg per 0.1 s (*=managed state changed): {string.Join(" ", rows)}");
    }

    /// <summary>The ECS animation controller's managed base state, as text, and its time field.</summary>
    private static (string State, float Time) R11State(ZModel m)
    {
        try
        {
            var comp = m.AnimComp?.TryCast<ECSAnimComp>();
            var c = comp?.controller_;
            if (c == null) return ($"noCtl({m.AnimComp?.GetIl2CppType().Name})", 0f);
            var s = c.baseState_;
            var u = c.upperState_;
            return ($"base(id={s.stateID} nt={s.normalizedTime:F3} t={s.time:F3} spd={s.speed:F2} fade={s.fadeDuration:F2} p=({s.parameters.x:F2},{s.parameters.y:F2})) " +
                    $"upper(id={u.stateID} nt={u.normalizedTime:F3}) ctlSpd={c.speed_:F2} persist={c.persistTime_:F2} cs={c.controllerState_?.ToString() ?? "-"}", s.time);
        }
        catch (Exception ex) { return ($"err {ex.GetType().Name}", 0f); }
    }

    private static string R11Input()
    {
        try
        {
            var m = Panda.ZGame.ZIgnoreMgr.Instance;
            return $"ignore Move={m.IsInputIgnore(Panda.ZGame.EInputMask.Move)} Skill={m.IsInputIgnore(Panda.ZGame.EInputMask.Skill)} " +
                   $"Rotation={m.IsInputIgnore(Panda.ZGame.EInputMask.Rotation)} byPause={m.IsInputIgnoreBySource(Panda.ZGame.EInputMask.Move, Panda.ZGame.EIgnoreMaskSource.EPayWebView)}";
        }
        catch (Exception ex) { return "err " + ex.Message; }
    }

    private IEnumerator StepEnd11()
    {
        Release("r11.freeze");
        Log($"R11 ECS worlds end {R11Worlds()}");
        yield break;
    }

    /// <summary>Every Unity.Entities world's time data (reflection: no compile-time Entities reference).</summary>
    private static string R11Worlds()
    {
        try
        {
            var worldType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.Entities.World")).FirstOrDefault(t => t != null);
            if (worldType == null) return "no Unity.Entities.World type";
            var all = worldType.GetProperty("All")?.GetValue(null);
            var parts = new List<string>();
            if (all is System.Collections.IEnumerable en)
                foreach (var w in en)
                {
                    var name = worldType.GetProperty("Name")?.GetValue(w);
                    var time = worldType.GetProperty("Time")?.GetValue(w);
                    var dt = time?.GetType().GetProperty("DeltaTime")?.GetValue(time) ?? time?.GetType().GetField("DeltaTime")?.GetValue(time);
                    var el = time?.GetType().GetProperty("ElapsedTime")?.GetValue(time) ?? time?.GetType().GetField("ElapsedTime")?.GetValue(time);
                    parts.Add($"{name}: dt={dt} elapsed={el}");
                }
            else parts.Add($"All={all?.GetType().Name}");
            return string.Join(" ; ", parts);
        }
        catch (Exception ex) { return "err " + ex.Message; }
    }
}
