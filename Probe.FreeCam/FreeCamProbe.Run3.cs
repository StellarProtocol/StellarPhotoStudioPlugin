using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 3 (owner 2026-10-01: freeze must cover "everything on screen" — monsters, NPCs, summons/pets too).
/// (1) <c>R3_entities</c>: every <c>ZEntityMgr</c> collection with counts, then every live entity in <c>EntityDict</c>
/// grouped by its IL2CPP class (MonsterEnt, NpcEnt, PetEnt, …) with distance, summon flag and model kind.
/// (2) Entity-appear hooks (Harmony postfixes on <c>ZEntityMgr.AddEntity</c>, <c>onAddEntity</c> and
/// <c>OnModelLoadFinish</c>), armed for the whole sequence, counting hits per class. Run3Freeze.cs uses them to freeze
/// entities that appear during a freeze. The local player is never frozen or held.
/// </summary>
public sealed partial class FreeCamProbe
{
    internal sealed record Ent(long Uuid, string Kind, int LuaType, float Dist, bool Summon);

    private const float R3Range = 40f;

    // Appear hooks — static because Harmony postfixes are static. 0 = count only, 1 = freeze from whichever hook fires first.
    private static int _apMode;
    private static FreeCamProbe? _apSelf;
    private static readonly Dictionary<string, int> _apHits = new();
    private static readonly List<(long Uuid, string Hook, float T, bool ModelAtHook)> _apEvents = new();
    private static int _apThreadId = -1;
    private Harmony? _apHarmony;

    private static ZEntityMgr? Mgr() => ZEntityMgr.IsCreated ? ZEntityMgr.Instance : null;

    private static string KindOf(ZEntity e)
    {
        try { return e.GetIl2CppType().Name; } catch { return "?"; }
    }

    /// <summary>Every live entity in EntityDict within <paramref name="maxDist"/> m (all distances when &lt; 0), self excluded.</summary>
    private List<Ent> AllEntities(float maxDist)
    {
        var list = new List<Ent>();
        var em = Mgr();
        var self = SelfEntity();
        var origin = LiveModel(self)?.GetAttrGoPosition();
        if (em == null || origin == null) return list;
        foreach (var uuid in DictKeys(em.EntityDict))
        {
            var e = em.GetEntity(uuid);
            if (e == null || e.IsDestroying || e.Uuid == self!.Uuid) continue;
            var m = LiveModel(e);
            var d = m == null ? -1f : Vector3.Distance(origin.Value, m.GetAttrGoPosition());
            if (maxDist >= 0f && (d < 0f || d > maxDist)) continue;
            bool summon = false; int lt = -1;
            try { summon = e.IsSummon; } catch { }
            try { lt = e.LuaEntType; } catch { }
            list.Add(new Ent(uuid, KindOf(e), lt, d, summon));
        }
        return list.OrderBy(x => x.Dist < 0 ? float.MaxValue : x.Dist).ToList();
    }

    private static List<long> DictKeys(Il2CppSystem.Collections.Generic.Dictionary<long, ZEntity>? dict)
    {
        var keys = new List<long>();
        if (dict == null) return keys;
        var en = dict.Keys.GetEnumerator();
        while (en.MoveNext()) keys.Add(en.Current);
        return keys;
    }

    private static ZEntity? EntByUuid(long uuid)
    {
        var e = Mgr()?.GetEntity(uuid);
        return e == null || e.IsDestroying ? null : e;
    }

    private IEnumerator StepEntities3()
    {
        var em = Mgr();
        if (em == null) { Log("R3 ZEntityMgr not created"); yield break; }
        ArmAppearHooks();   // re-armed per step (RunOne releases every armed hook after each step)
        LogCollections(em);
        var all = AllEntities(-1f);
        Log($"R3 EntityDict live (excl. self): {all.Count}");
        foreach (var g in all.GroupBy(x => x.Kind).OrderByDescending(g => g.Count()))
        {
            var withModel = g.Where(x => x.Dist >= 0f).ToList();
            var inRange = withModel.Count(x => x.Dist <= R3Range);
            var near = withModel.FirstOrDefault();
            var kind = near == null ? "no model" : ModelKind(LiveModel(EntByUuid(near.Uuid))!);
            Log($"R3 kind {g.Key} luaType={g.First().LuaType} n={g.Count()} withModel={withModel.Count} within{R3Range:F0}m={inRange} summons={g.Count(x => x.Summon)} " +
                $"nearest={(near == null ? "-" : $"{near.Uuid}@{near.Dist:F1}m")} nearestModel[{kind}] " +
                $"anim[{(near == null ? "-" : EntAnimRead(near.Uuid))}]");
        }
        foreach (var x in all.Where(x => x.Dist >= 0f && x.Dist <= R3Range && x.Kind != "CharEnt").Take(25))
            Log($"R3 near {x.Kind} uuid={x.Uuid} d={x.Dist:F1}m summon={x.Summon} {SummonerText(em, x)}");
        Log($"R3 appear hooks so far: {ApHitsText()}");
        yield break;
    }

    private void LogCollections(ZEntityMgr em)
    {
        string C(string name, Func<int> f) { try { return $"{name}={f()}"; } catch (Exception ex) { return $"{name}=ERR:{ex.GetType().Name}"; } }
        Log("R3 collections: " + string.Join(" ", new[]
        {
            C("EntityDict", () => em.EntityDict.Count), C("CharIdList", () => em.CharIdList.Count),
            C("NpcDict", () => em.NpcDict.Count), C("MonsterDict", () => em.MonsterDict.Count), C("BossDict", () => em.BossDict.Count),
            C("PetDict", () => em.PetDict.Count), C("ToyDict", () => em.ToyDict.Count), C("CollectionDict", () => em.CollectionDict.Count),
            C("ZoneDict", () => em.ZoneDict.Count), C("CanHitSceneObjDict", () => em.CanHitSceneObjDict.Count),
            C("CanHitCharDict", () => em.CanHitCharDict.Count), C("levelEntityDict_", () => em.levelEntityDict_.Count),
            C("summonDict_", () => em.summonDict_.Count), C("visibleCharSet_", () => em.visibleCharSet_.Count),
            C("delayRemoveEntityDict_", () => em.delayRemoveEntityDict_.Count), C("firstEnterEntitySet_", () => em.firstEnterEntitySet_.Count),
        }));
    }

    private static string SummonerText(ZEntityMgr em, Ent x)
    {
        try
        {
            if (!em.TryGetSummonerSet(x.Uuid, out var set) || set == null) return "summonerOf=0";
            return $"summonerOf={set.Count}";
        }
        catch (Exception ex) { return $"summonerOf=ERR:{ex.GetType().Name}"; }
    }

    /// <summary>Anim readback for any entity; each term isolated (Toy/Collection storages lack the SkillStage component).</summary>
    private static string EntAnimRead(long uuid)
    {
        var e = EntByUuid(uuid);
        var m = LiveModel(e);
        if (e == null) return "gone";
        string R(Func<string> f) { try { return f(); } catch (Exception ex) { return "ERR:" + ex.GetType().Name; } }
        return $"comp.Speed={R(() => m?.AnimComp?.Speed.ToString("F2") ?? "null")} attrAnimSpeed={R(() => EntityAttrExtensions.GetAttrAnimSpeed(e).ToString("F2"))} " +
               $"skillStage={R(() => EntityAttrExtensions.GetAttrSkillStageTimeFactor(e).ToString("F2"))}";
    }

    private void ArmAppearHooks()
    {
        if (_apHarmony != null) return;
        _apSelf = this;
        try
        {
            _apHarmony = _services.Harmony.Create("freecamprobe.appear");
            var t = typeof(ZEntityMgr);
            Patch(t, "AddEntity", nameof(ApAddEntityPostfix));
            Patch(t, "onAddEntity", nameof(ApOnAddEntityPostfix));
            Patch(t, "OnModelLoadFinish", nameof(ApModelLoadPostfix));
            Patch(t, "EnterView", nameof(ApEnterViewPostfix));
        }
        catch (Exception ex) { Log($"R3 appear hook arm FAILED {ex.GetType().Name}: {ex.Message}"); }
        Arm("r3.appearhooks", () => { _apMode = 0; try { _apHarmony?.UnpatchSelf(); } catch { } _apHarmony = null; _apSelf = null; });
    }

    private void Patch(Type t, string method, string postfix)
    {
        var m = AccessTools.Method(t, method);
        if (m == null) { Log($"R3 hook {method}: method not found"); return; }
        var post = new HarmonyMethod(typeof(FreeCamProbe).GetMethod(postfix, BindingFlags.NonPublic | BindingFlags.Static));
        _apHarmony!.Patch(m, postfix: post);
        Log($"R3 hook armed: postfix {m}");
    }

    private static void ApCount(string hook, ZEntity? e, long uuid)
    {
        if (_apThreadId < 0) _apThreadId = Environment.CurrentManagedThreadId;
        var key = $"{hook}:{(e == null ? "null" : KindOf(e))}";
        _apHits[key] = _apHits.TryGetValue(key, out var n) ? n + 1 : 1;
        if (_apEvents.Count < 4000) _apEvents.Add((uuid, hook, Time.realtimeSinceStartup, LiveModel(e) != null));
    }

    private static void ApAddEntityPostfix(ZEntity e)
    {
        try
        {
            ApCount("AddEntity", e, e?.Uuid ?? 0);
            if (_apMode == 1 && e != null) _apSelf?.FreezeAppeared(e.Uuid, "AddEntity");
        }
        catch { }
    }

    private static void ApEnterViewPostfix(long uuid)
    {
        try { ApCount("EnterView", EntByUuid(uuid), uuid); } catch { }
    }

    private static void ApOnAddEntityPostfix(ZEntity e)
    {
        try
        {
            ApCount("onAddEntity", e, e?.Uuid ?? 0);
            if (_apMode == 1 && e != null) _apSelf?.FreezeAppeared(e.Uuid, "onAddEntity");
        }
        catch { }
    }

    private static void ApModelLoadPostfix(long uuid)
    {
        try
        {
            var e = EntByUuid(uuid);
            ApCount("OnModelLoadFinish", e, uuid);
            if (_apMode == 1 && e != null) _apSelf?.FreezeAppeared(uuid, "OnModelLoadFinish");
        }
        catch { }
    }

    private static string ApHitsText() =>
        _apHits.Count == 0 ? "none" : string.Join(" ", _apHits.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")) + $" thread={_apThreadId} main={_mainThreadId}";

    private static int _mainThreadId = -1;
}
