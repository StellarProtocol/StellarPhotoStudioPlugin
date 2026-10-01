using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Panda.ZEffect;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 2 / B — effect freeze with visual + numeric proof. (1) Hooks the creation path (Harmony prefix on every
/// <c>ZEffectManager.AddEffect</c> overload → <c>context.IsFreeze = true</c> while mode 1; postfix on
/// <c>AddEffectDisplay(ZEffect)</c> → <c>SetEffectFreeze(true)</c> while mode 2) and logs every creation. (2) Lists
/// EffectDict with owner / position / particle systems. (3) Looks for an emote that spawns an effect on the local
/// player; else uses the nearest on-screen ambient effect. (4) Unfrozen vs frozen t1/t2 pixel diff of that region with
/// the player's animation held by AnimComp.Speed=0 in BOTH conditions (isolates the effect), plus ParticleSystem.time
/// sums (frozen ⇒ unchanged). (5) createdWhileFrozen under each hook mode: how many end up frozen.
/// Every flagged / frozen uid is unfrozen in the release.
/// </summary>
public sealed partial class FreeCamProbe
{
    private static int _fxMode;   // 0 off, 1 = context.IsFreeze at AddEffect, 2 = SetEffectFreeze at AddEffectDisplay
    private static int _fxAddHits, _fxCtxFlagged, _fxDisplayHits, _fxDisplayFrozen;
    private static readonly List<long> _fxCreated = new();
    private Harmony? _fxHarmony;
    private readonly HashSet<long> _fxTouched = new();

    private IEnumerator StepEffects2()
    {
        var epoch = _sceneEpoch;
        var em = ZEffectManager.IsCreated ? ZEffectManager.Instance : null;
        if (em == null) { Log("B ZEffectManager not created"); yield break; }
        ArmFxHooks();
        Arm("fx.touched", UnfreezeTouched);
        try
        {
            var list = ListEffects(em, "B dict at start");
            // (3) effect source: run 2 showed no candidate emote (9189/10039/9083/9001) spawns an effect, so use the
            // game's own client-side "scan" world effect on the player (env_vm.lua: PlayerEnt:PlayWorldEffectBySelf).
            var n0 = _fxCreated.Count;
            var spawned = SpawnSelfEffect();
            yield return Wait(0.6f);
            var mine = SelfEffects(em, _fxCreated.Skip(n0));
            Log($"B spawn check ({spawned}): created={_fxCreated.Count - n0} self/near(<5m)={mine.Count} [{string.Join(" ", mine.Take(6).Select(u => FxText(em, u)))}]");
            var fxEmote = mine.Count > 0 ? 1 : 0;   // 1 = spawn the world effect per trial
            yield return Wait(3f);                  // let it expire

            // (4) unfrozen vs frozen
            yield return FxTrial(em, "B_U_unfrozen", fxEmote, false, epoch);
            yield return FxTrial(em, "B_F_frozen", fxEmote, true, epoch);

            // (5) created while frozen, per hook mode
            yield return FxCreatedWhileFrozen(em, 1, fxEmote, epoch);
            yield return FxCreatedWhileFrozen(em, 2, fxEmote, epoch);
            Log($"B hooks: addHits={_fxAddHits} ctxFlagged={_fxCtxFlagged} displayHits={_fxDisplayHits} displayFrozen={_fxDisplayFrozen} created={_fxCreated.Count}");
        }
        finally
        {
            Release("fx.touched");
            Release("fx.hooks");
        }
        ListEffects(em, "B dict at end (after release)");
    }

    private IEnumerator FxTrial(ZEffectManager em, string name, int fxEmote, bool freeze, int epoch)
    {
        if (Aborted(epoch)) yield break;
        var n0 = _fxCreated.Count;
        long target = 0;
        RectInt? region = null;
        if (fxEmote != 0)
        {
            Log($"{name} spawn: {SpawnSelfEffect()}");
            var until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until && SelfEffects(em, _fxCreated.Skip(n0)).Count == 0) yield return null;
            yield return Wait(0.3f);
            target = SelfEffects(em, _fxCreated.Skip(n0)).FirstOrDefault();
            region = SelfRegion(0.18f, 0.25f);
        }
        if (target == 0)
        {
            target = NearestOnScreenEffect(em, out var pos);
            var cam = MainCam();
            if (target != 0 && cam != null) region = RegionAround(cam, pos, 0.07f, 0.10f);
        }
        if (target == 0 || region == null) { Log($"{name}: no target effect on screen — n/a"); yield break; }

        var hold = ApplyEntityFactor(NearbyChars(1f, 1, includeSelf: true), skillStage: true);   // self animation held in BOTH conditions (A: P5a freezes, P1 does not)
        if (hold != null) Arm("fx.animhold", hold);
        var frozen = new List<long>();
        try
        {
            if (freeze)
            {
                foreach (var uid in EffectKeys(em, out _))
                {
                    try { em.SetEffectFreeze(uid, true); frozen.Add(uid); _fxTouched.Add(uid); } catch { }
                }
            }
            yield return Frames(2);
            var p1 = ParticleState(em, target);
            var t1 = CaptureMany($"{name}_t1", region.Value);
            yield return Wait(0.6f);
            var p2 = ParticleState(em, target);
            var t2 = CaptureMany($"{name}_t2", region.Value);
            var (m, pct) = Diff(t1?[0], t2?[0]);
            Log($"{name} target={FxText(em, target)} frozenAll={frozen.Count} diff meanAbs={m:F2} changed={pct:F1}% " +
                $"particles t1[{p1}] t2[{p2}] particleTimeAdvanced={(p1.Time >= 0 && p2.Time >= 0 ? (p2.Time - p1.Time).ToString("F3") : "n/a")}s");
        }
        finally
        {
            foreach (var uid in frozen) { try { em.SetEffectFreeze(uid, false); } catch { } }
            Release("fx.animhold");
        }
        yield return Wait(3f);
    }

    private IEnumerator FxCreatedWhileFrozen(ZEffectManager em, int mode, int fxEmote, int epoch)
    {
        if (Aborted(epoch)) yield break;
        var before = EffectKeys(em, out _);
        foreach (var uid in before) { try { em.SetEffectFreeze(uid, true); _fxTouched.Add(uid); } catch { } }
        var n0 = _fxCreated.Count;
        _fxMode = mode;
        try
        {
            Log($"B cwf mode{mode} spawn: {SpawnSelfEffect()}");
            yield return Wait(1.0f);
        }
        finally { _fxMode = 0; }
        var created = EffectKeys(em, out _).Except(before).ToList();
        foreach (var u in created) _fxTouched.Add(u);
        var hooked = _fxCreated.Skip(n0).ToList();
        var flagged = created.Count(u => FxIsFreeze(em, u) == true);
        var states1 = created.Take(8).Select(u => (u, ParticleState(em, u))).ToList();
        yield return Wait(0.5f);
        var advanced = states1.Count(s => { var p = ParticleState(em, s.u); return s.Item2.Time >= 0 && p.Time - s.Item2.Time > 0.05f; });
        var measurable = states1.Count(s => s.Item2.Time >= 0);
        Log($"B createdWhileFrozen mode{mode}({(mode == 1 ? "AddEffect prefix: context.IsFreeze=true" : "AddEffectDisplay postfix: SetEffectFreeze")}) " +
            $"created={created.Count} viaHook={hooked.Count} contextIsFreeze={flagged}/{created.Count} particlesStillAdvancing={advanced}/{measurable} " +
            $"[{string.Join(" ", created.Take(6).Select(u => FxText(em, u)))}]");
        foreach (var uid in _fxTouched) { try { em.SetEffectFreeze(uid, false); } catch { } }
        yield return Wait(3f);
    }

    /// <summary>The game's own client-side world effect on the local player (resonance "scan", env_vm.lua) — 3 s.</summary>
    private string SpawnSelfEffect()
    {
        var e = SelfEntity();
        if (e == null) return "no self";
        try { e.PlayWorldEffectBySelf("effect/character/p_fx_saomiao", 3f); return "PlayWorldEffectBySelf(effect/character/p_fx_saomiao, 3)"; }
        catch (Exception ex) { return $"PlayWorldEffectBySelf FAILED {ex.GetType().Name}: {Short(ex.Message)}"; }
    }

    private void ArmFxHooks()
    {
        _fxAddHits = _fxCtxFlagged = _fxDisplayHits = _fxDisplayFrozen = 0;
        _fxCreated.Clear();
        _fxMode = 0;
        var t = typeof(ZEffectManager);
        var sigs = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "AddEffect" or "AddEffectDisplay" or "LoadEffect" or "AddWeaponEffect" or "AddScreenEffect" or "AddIndicatorEffect")
            .Select(m => m.ToString());
        Log("B creation signatures: " + string.Join(" | ", sigs));
        try
        {
            _fxHarmony = _services.Harmony.Create("freecamprobe.fx");
            var pre = new HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(FxAddPrefix), BindingFlags.NonPublic | BindingFlags.Static));
            var post = new HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(FxAddPostfix), BindingFlags.NonPublic | BindingFlags.Static));
            var dpost = new HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(FxDisplayPostfix), BindingFlags.NonPublic | BindingFlags.Static));
            var single = AccessTools.Method(t, "AddEffect", new[] { typeof(EffectContext) });
            _fxHarmony.Patch(single, prefix: pre, postfix: post);
            foreach (var host in new[] { typeof(ZEntity), typeof(ZModel) })
            {
                var m = AccessTools.Method(t, "AddEffect", new[] { host, typeof(EffectContext) });
                if (m != null) _fxHarmony.Patch(m, prefix: pre);
            }
            var disp = AccessTools.Method(t, "AddEffectDisplay", new[] { typeof(ZEffect) });
            if (disp != null) _fxHarmony.Patch(disp, postfix: dpost);
            Log("B hooks armed: AddEffect(ctx) prefix+postfix, AddEffect(ZEntity|ZModel,ctx) prefix, AddEffectDisplay(ZEffect) postfix");
        }
        catch (Exception ex) { Log($"B hook arm FAILED {ex.GetType().Name}: {ex.Message}"); }
        Arm("fx.hooks", () => { _fxMode = 0; try { _fxHarmony?.UnpatchSelf(); } catch { } _fxHarmony = null; });
    }

    private static void FxAddPrefix(EffectContext context)
    {
        _fxAddHits++;
        if (_fxMode == 1 && context != null) { try { context.IsFreeze = true; _fxCtxFlagged++; } catch { } }
    }

    private static void FxAddPostfix(long __result)
    {
        if (_fxCreated.Count < 5000) _fxCreated.Add(__result);
    }

    private static void FxDisplayPostfix(ZEffect effect)
    {
        _fxDisplayHits++;
        if (_fxMode == 2 && effect != null) { try { effect.SetEffectFreeze(true); _fxDisplayFrozen++; } catch { } }
    }

    private void UnfreezeTouched()
    {
        var em = ZEffectManager.IsCreated ? ZEffectManager.Instance : null;
        if (em == null) return;
        foreach (var uid in _fxTouched) { try { em.SetEffectFreeze(uid, false); } catch { } }
        Log($"B unfroze {_fxTouched.Count} touched uids");
        _fxTouched.Clear();
    }

    internal readonly record struct PState(float Time, int Particles, int Systems, int Paused)
    {
        public override string ToString() => Systems < 0 ? "no-go" : $"ps={Systems} paused={Paused} time={Time:F3} n={Particles}";
    }

    private static PState ParticleState(ZEffectManager em, long uid)
    {
        try
        {
            var go = em.GetEffect(uid)?.GetGo();
            if (go == null) return new PState(-1f, 0, -1, 0);
            float t = 0f; int n = 0, paused = 0;
            var arr = go.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in arr) { t += ps.time; n += ps.particleCount; if (ps.isPaused) paused++; }
            return new PState(arr.Length == 0 ? -1f : t, n, arr.Length, paused);
        }
        catch { return new PState(-1f, 0, -1, 0); }
    }

    private static bool? FxIsFreeze(ZEffectManager em, long uid)
    {
        try { return em.GetEffect(uid)?.Context?.IsFreeze; } catch { return null; }
    }

    private string FxText(ZEffectManager em, long uid)
    {
        try
        {
            var fx = em.GetEffect(uid);
            if (fx == null) return $"{uid}:gone";
            var ctx = fx.Context;
            var go = fx.GetGo();
            var pos = go != null ? go.transform.position : ctx?.Position ?? Vector3.zero;
            var self = LiveModel(SelfEntity());
            var d = self == null ? -1f : Vector3.Distance(self.GetAttrGoPosition(), pos);
            return $"{uid}:addr={ctx?.Addr} belong={ctx?.BelongUuid} st={fx.LoadStatus} freeze={ctx?.IsFreeze} d={d:F1}m go={(go == null ? "null" : go.name)} {ParticleState(em, uid)}";
        }
        catch (Exception ex) { return $"{uid}:err {ex.GetType().Name}"; }
    }

    /// <summary>Effects (from <paramref name="uids"/>) owned by the local player or within 5 m of it.</summary>
    private List<long> SelfEffects(ZEffectManager em, IEnumerable<long> uids)
    {
        var res = new List<long>();
        var self = SelfEntity();
        var m = LiveModel(self);
        if (self == null || m == null) return res;
        var p = m.GetAttrGoPosition();
        foreach (var uid in uids)
        {
            try
            {
                var fx = em.GetEffect(uid);
                if (fx == null) continue;
                var ctx = fx.Context;
                var go = fx.GetGo();
                var pos = go != null ? go.transform.position : ctx?.Position ?? Vector3.zero;
                if (ctx?.BelongUuid == self.Uuid || Vector3.Distance(pos, p) < 5f) res.Add(uid);
            }
            catch { }
        }
        return res;
    }

    private long NearestOnScreenEffect(ZEffectManager em, out Vector3 pos)
    {
        pos = Vector3.zero;
        var cam = MainCam();
        var self = LiveModel(SelfEntity());
        if (cam == null || self == null) return 0;
        var p = self.GetAttrGoPosition();
        long best = 0;
        var bestD = float.MaxValue;
        foreach (var uid in EffectKeys(em, out _))
        {
            try
            {
                var go = em.GetEffect(uid)?.GetGo();
                if (go == null || !go.activeInHierarchy) continue;
                if (ParticleState(em, uid).Systems <= 0) continue;
                var wp = go.transform.position;
                var vp = cam.WorldToViewportPoint(wp);
                if (vp.z <= 0f || vp.x < 0.1f || vp.x > 0.9f || vp.y < 0.1f || vp.y > 0.9f) continue;
                var d = Vector3.Distance(wp, p);
                if (d < bestD) { bestD = d; best = uid; pos = wp; }
            }
            catch { }
        }
        return best;
    }

    private List<long> ListEffects(ZEffectManager em, string tag)
    {
        var keys = EffectKeys(em, out _);
        var self = LiveModel(SelfEntity());
        var p = self?.GetAttrGoPosition() ?? Vector3.zero;
        var rows = keys.Select(u =>
        {
            float d;
            try { var go = em.GetEffect(u)?.GetGo(); d = go == null ? 9999f : Vector3.Distance(go.transform.position, p); } catch { d = 9999f; }
            return (u, d);
        }).OrderBy(r => r.d).ToList();
        Log($"{tag}: count={keys.Count} within5m={rows.Count(r => r.d < 5f)} within30m={rows.Count(r => r.d < 30f)} nearest: {string.Join(" || ", rows.Take(8).Select(r => FxText(em, r.u)))}");
        return keys;
    }
}
