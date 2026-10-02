using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Panda.ZGame;
using ZPureEntity = Panda.ZGame.Pure.ZPureEntity;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 7 hooks — WHO undoes the scene freeze on monsters. (1) A static map: the interop's <c>CallerCount</c> of each attr/speed writer and of the skill-stage entry
/// points (the runtime <c>XrefScanner.UsedBy</c> hung the main thread in run 7a and is not used). <c>SetAttrSkillStageTimeFactor</c> has <c>CallerCount(0)</c> = inlined at every native call site, so
/// a Harmony hook on it can only see our own (reflective) writes — process rules § 15: it is armed anyway and its first
/// N calls are logged raw so silence is attributable. (2) Harmony prefixes/postfixes that log entity, value, frame and
/// a "context" stack (bracket hooks on the callers from the map) while the freeze is on.
/// </summary>
public sealed partial class FreeCamProbe
{
    private static FreeCamProbe? _r7;
    private static string _r7Phase = "off";          // off | base | frozen | post — hook logging gate + count bucket
    private static int _r7Frame0;
    private static readonly Stack<string> _r7Ctx = new();
    private static readonly Dictionary<string, int> _r7Hits = new();
    private static readonly Dictionary<string, int> _r7LogCap = new();
    private static readonly HashSet<long> _r7Tracked = new();
    private Harmony? _r7Harmony;
    private readonly List<MethodBase> _r7Brackets = new();

    private static readonly string[] R7Candidates =
    {
        "PlaySkillStage", "PlayPassiveSkillStage", "doSkillStage", "syncSkillStageEnd", "InterruptSkillStage",
        "forceChangeSkill", "onSyncSkillStageTrigger", "SyncServerSkillStageEnd",
    };

    private static int R7F() => Time.frameCount - _r7Frame0;

    // Run 7c hung the main thread one frame after the hooks went live: _r7Hits/_r7Ctx were mutated from the main thread
    // (set_Speed) and the network thread (WorldNtfImpl brackets) at once — a corrupted Dictionary loops forever. Every
    // shared structure is now under one lock, and the context stack is main-thread only.
    private static readonly object _r7Lock = new();
    private static bool R7Main() => Environment.CurrentManagedThreadId == _mainThreadId;

    private static void R7Hit(string key)
    {
        var k = _r7Phase + ":" + key + (R7Main() ? "" : "@offmain");
        lock (_r7Lock) _r7Hits[k] = _r7Hits.TryGetValue(k, out var n) ? n + 1 : 1;
    }

    /// <summary>True for the first <paramref name="cap"/> uses of <paramref name="key"/>.</summary>
    private static bool R7Cap(string key, int cap)
    {
        lock (_r7Lock)
        {
            _r7LogCap.TryGetValue(key, out var n);
            if (n >= cap) return false;
            _r7LogCap[key] = n + 1;
            return true;
        }
    }

    private static string R7CtxText() => !R7Main() ? "(offmain)" : _r7Ctx.Count == 0 ? "-" : string.Join(">", _r7Ctx.Reverse().Take(4));

    private static string R7Ent(ZPureEntity? p)
    {
        try
        {
            var e = p?.TryCast<ZEntity>();
            return e == null ? "?" : $"{KindOf(e)}:{e.Uuid}{(e.Uuid == _r7?._selfUuid ? "(self)" : "")}";
        }
        catch { return "ERR"; }
    }

    private static bool R7IsTracked(long u) { lock (_r7Lock) return _r7Tracked.Contains(u); }

    private static long R7Uuid(ZPureEntity? p)
    {
        try { return p?.TryCast<ZEntity>()?.Uuid ?? 0; } catch { return 0; }
    }

    // ---- value hooks ---------------------------------------------------------------------------------------------

    private static void R7FactorPrefix(ZPureEntity entity, float value) => R7AttrWrite("SetAttrSkillStageTimeFactor", entity, value);
    private static void R7InitFactorPrefix(ZPureEntity entity, float value) => R7AttrWrite("InitAttrSkillStageTimeFactor", entity, value);
    private static void R7BfsPrefix(ZPureEntity entity, float value) => R7AttrWrite("SetAttrBattleFrameSpeed", entity, value);
    private static void R7ResPrefix(ZPureEntity entity, float value) => R7AttrWrite("SetAttrAnimResFactor", entity, value);

    private static void R7AttrWrite(string name, ZPureEntity entity, float value)
    {
        if (_r7Phase == "off") return;
        try
        {
            R7Hit(name);
            // Every call while armed is raw-logged up to a cap (§ 15): the framework's own writes are the 0 at freeze and
            // the prior on release; anything else is the game writing (if the hook can see it at all).
            if (R7Cap("w:" + name, 80))
                _r7?.Log($"R7H {name} {_r7Phase} f{R7F()} ent={R7Ent(entity)} value={value:F2} ctx={R7CtxText()}");
        }
        catch { }
    }

    private static void R7DirtyPrefix(ZPureEntity entity, bool includeSpeedType)
    {
        if (_r7Phase == "off") return;
        try
        {
            R7Hit("SetAttrAnimSpeedDirty");
            var u = R7Uuid(entity);
            if (_r7Phase == "frozen" && R7IsTracked(u) && R7Cap("dirty:" + u, 6))
                _r7?.Log($"R7H SetAttrAnimSpeedDirty frozen f{R7F()} ent={R7Ent(entity)} inclType={includeSpeedType} ctx={R7CtxText()}");
        }
        catch { }
    }

    private static void R7TryCalcPostfix(ZEntity entity)
    {
        if (_r7Phase == "off") return;
        try
        {
            R7Hit("tryCalculateAnimSpeed");
            if (_r7Phase != "frozen" || entity == null || !R7IsTracked(entity.Uuid)) return;
            var after = EntityAttrExtensions.GetAttrAnimSpeed(entity);
            if (after <= 0.001f) return;
            R7Hit("tryCalculateAnimSpeed>0");
            if (R7Cap("calc:" + entity.Uuid, 8))
                _r7?.Log($"R7H tryCalculateAnimSpeed frozen f{R7F()} ent={KindOf(entity)}:{entity.Uuid} animSpeed->{after:F2} " +
                         $"fac={Safe(() => EntityAttrExtensions.GetAttrSkillStageTimeFactor(entity).ToString("F2"))} " +
                         $"bfs={Safe(() => EntityAttrExtensions.GetAttrBattleFrameSpeed(entity).ToString("F2"))} " +
                         $"skill={Safe(() => EntityAttrExtensions.GetSkillId(entity).ToString())} ctx={R7CtxText()}");
        }
        catch { }
    }

    private static void R7SpeedPrefix(AnimCompBase __instance, float value)
    {
        if (_r7Phase == "off") return;
        try
        {
            R7Hit("set_Speed");
            if (_r7Phase != "frozen" || value <= 0.001f) return;
            var host = __instance?.Model?.Host;
            if (host == null || !R7IsTracked(host.Uuid)) return;
            R7Hit("set_Speed>0(tracked)");
            if (R7Cap("speed:" + host.Uuid, 10))
                _r7?.Log($"R7H set_Speed frozen f{R7F()} ent={KindOf(host)}:{host.Uuid} value={value:F2} " +
                         $"fac={Safe(() => EntityAttrExtensions.GetAttrSkillStageTimeFactor(host).ToString("F2"))} " +
                         $"attrAnim={Safe(() => EntityAttrExtensions.GetAttrAnimSpeed(host).ToString("F2"))} " +
                         $"skill={Safe(() => EntityAttrExtensions.GetSkillId(host).ToString())} ctx={R7CtxText()}");
        }
        catch { }
    }

    // ---- context brackets ----------------------------------------------------------------------------------------

    private static void R7CtxPre(MethodBase __originalMethod)
    {
        if (_r7Phase == "off") return;
        try
        {
            var n = __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
            if (R7Main() && _r7Ctx.Count < 16) _r7Ctx.Push(n);
            R7Hit("ctx " + n);
            if (_r7Phase == "frozen" && R7Cap("ctx:" + n, 3)) _r7?.Log($"R7H ctx-enter frozen f{R7F()} {n} stack={R7CtxText()}");
        }
        catch { }
    }

    private static void R7CtxPost()
    {
        if (_r7Phase == "off" || !R7Main()) return;
        if (_r7Ctx.Count > 0) _r7Ctx.Pop();
    }

    // ---- static map + arming -------------------------------------------------------------------------------------

    private static string R7Name(MethodBase? m) => m == null ? "null" : $"{m.DeclaringType?.FullName}.{m.Name}({m.GetParameters().Length})";

    private static int R7CallerCount(MethodBase m)
    {
        try
        {
            var a = m.GetCustomAttributes(false).FirstOrDefault(x => x.GetType().Name == "CallerCountAttribute");
            return a == null ? -1 : Convert.ToInt32(a.GetType().GetField("Count")?.GetValue(a) ?? a.GetType().GetProperty("Count")?.GetValue(a) ?? -1);
        }
        catch { return -2; }
    }

    /// <summary>Logs the static map and fills <see cref="_r7Brackets"/> (callers to bracket at arm time).</summary>
    private void R7StaticMap()
    {
        var ext = typeof(EntityAttrExtensions);
        var asm = ext.Assembly;
        var targets = new List<MethodBase>();
        void Add(MethodBase? m, string what) { if (m == null) Log($"R7 MAP target {what}: NOT FOUND"); else targets.Add(m); }
        Add(typeof(AnimCompBase).GetProperty("Speed")?.SetMethod, "AnimCompBase.set_Speed");
        Add(ext.GetMethod("tryCalculateAnimSpeed", BindingFlags.Public | BindingFlags.Static), "tryCalculateAnimSpeed");
        foreach (var n in new[] { "SetAttrSkillStageTimeFactor", "InitAttrSkillStageTimeFactor", "SetAttrAnimSpeedDirty", "SetAttrBattleFrameSpeed", "SetAttrAnimResFactor" })
            Add(ext.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == n), n);
        Type[] types;
        try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
        var accessors = new List<MethodBase>();
        var candidates = new List<MethodBase>();
        foreach (var t in types)
        {
            MethodInfo[] ms;
            try { ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); }
            catch { continue; }
            foreach (var m in ms)
            {
                if (m.Name.Contains("LocalAttrSkillStageTimeFactorComponent") && accessors.Count < 30) accessors.Add(m);
                if (Array.IndexOf(R7Candidates, m.Name) >= 0 && candidates.Count < 30) candidates.Add(m);
            }
        }
        Log($"R7 MAP accessors={accessors.Count} candidates={candidates.Count}");
        // Run 7a: Il2CppInterop's runtime XrefScanner.UsedBy hung the main thread on the first call (log ends at the
        // "R7 MAP accessors=" line, no further frame) — no runtime xref scan; caller counts only, candidates bracketed.
        var brackets = new List<MethodBase>();
        foreach (var m in targets.Concat(accessors))
            Log($"R7 MAP {R7Name(m)} callerCount={R7CallerCount(m)}");
        foreach (var m in candidates)
        {
            Log($"R7 MAP candidate {R7Name(m)} callerCount={R7CallerCount(m)}");
            // Network-thread handlers (Zservice.WorldNtfImpl) are not bracketed: the context stack is main-thread only, and
            // run 7c hung with them armed.
            if (m.DeclaringType?.Namespace == "Zservice") continue;
            if (!brackets.Contains(m)) brackets.Add(m);
        }
        _r7Brackets.Clear();
        _r7Brackets.AddRange(brackets.Where(b => !b.IsAbstract && !b.ContainsGenericParameters && !(b.DeclaringType?.ContainsGenericParameters ?? true)).Take(45));
        Log($"R7 MAP bracket set ({_r7Brackets.Count}): {string.Join(" | ", _r7Brackets.Select(R7Name))}");
    }

    private void R7ArmHooks()
    {
        if (_r7Harmony != null) return;
        _r7 = this;
        _r7Harmony = _services.Harmony.Create("freecamprobe.r7");
        var ext = typeof(EntityAttrExtensions);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        HarmonyMethod H(string n) => new(typeof(FreeCamProbe).GetMethod(n, flags));
        void P(MethodBase? target, string what, HarmonyMethod? pre, HarmonyMethod? post = null)
        {
            if (target == null) { Log($"R7 HOOK {what}: target not found"); return; }
            try { _r7Harmony!.Patch(target, prefix: pre, postfix: post); Log($"R7 HOOK armed {what} callerCount={R7CallerCount(target)}"); }
            catch (Exception ex) { Log($"R7 HOOK {what} FAILED {ex.GetType().Name}: {Short(ex.Message)}"); }
        }
        MethodInfo? S(string n) => ext.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == n);
        P(S("SetAttrSkillStageTimeFactor"), "SetAttrSkillStageTimeFactor", H(nameof(R7FactorPrefix)));
        P(S("InitAttrSkillStageTimeFactor"), "InitAttrSkillStageTimeFactor", H(nameof(R7InitFactorPrefix)));
        P(S("SetAttrBattleFrameSpeed"), "SetAttrBattleFrameSpeed", H(nameof(R7BfsPrefix)));
        P(S("SetAttrAnimResFactor"), "SetAttrAnimResFactor", H(nameof(R7ResPrefix)));
        P(S("SetAttrAnimSpeedDirty"), "SetAttrAnimSpeedDirty", H(nameof(R7DirtyPrefix)));
        P(S("tryCalculateAnimSpeed"), "tryCalculateAnimSpeed", null, H(nameof(R7TryCalcPostfix)));
        P(typeof(AnimCompBase).GetProperty("Speed")?.SetMethod, "AnimCompBase.set_Speed", H(nameof(R7SpeedPrefix)));
        var ok = 0;
        foreach (var b in _r7Brackets)
        {
            try { _r7Harmony.Patch(b, prefix: H(nameof(R7CtxPre)), postfix: H(nameof(R7CtxPost))); ok++; }
            catch (Exception ex) { Log($"R7 HOOK bracket {R7Name(b)} FAILED {ex.GetType().Name}: {Short(ex.Message)}"); }
        }
        Log($"R7 HOOK brackets armed {ok}/{_r7Brackets.Count}");
        Arm("r7.hooks", R7DisarmHooks);
    }

    private void R7DisarmHooks()
    {
        _r7Phase = "off";
        try { _r7Harmony?.UnpatchSelf(); } catch { }
        _r7Harmony = null;
        _r7Ctx.Clear();
    }

    private void R7LogHits(string label)
    {
        string text;
        lock (_r7Lock) text = _r7Hits.Count == 0 ? "none" : string.Join(" ", _r7Hits.OrderBy(k => k.Key).Select(k => $"[{k.Key}]={k.Value}"));
        Log($"R7 HITS {label}: {text}");
    }
}
