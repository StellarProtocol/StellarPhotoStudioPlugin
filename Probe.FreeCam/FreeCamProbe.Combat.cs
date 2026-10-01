using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Panda.ZGame;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// § 9 item 5 — the local-player combat flag. Recon: the game HUD (fighterbtns_view.lua) watches wire attr
/// <c>AttrCombatState</c> (= 104, enum_e_attr_type.proto) via <c>BindEntityLuaAttrWatcher</c>, plus the local
/// <c>AttrInBattleShow</c>; C# reads are <c>ZEntity.GetLuaIsInCombat()</c> / <c>GetLuaLocalAttrInBattleShow()</c> /
/// <c>EntityAttrExtensions.GetIsInCombat</c>. Event candidates armed for the whole run:
/// (1) framework <c>ICombatEvents</c> → <c>EntityAttributesChanged</c> for the local player carrying attr 104/114;
/// (2) Harmony postfixes on <c>EntityAttrExtensions.SetLocalCombatData</c> / <c>SetLocalAttrInBattleShow</c>.
/// </summary>
public sealed partial class FreeCamProbe
{
    private const int AttrCombatState = 104;
    private const int AttrCombatStateTime = 114;

    private static FreeCamProbe? _self;
    private Harmony? _harmony;
    private readonly HashSet<int> _selfAttrIds = new();
    private int _selfAttrEvents;
    private readonly List<string> _combatAttrSeen = new();
    private int _setLocalCombatHits;
    private int _setInBattleShowHits;
    private long _selfUuid;

    private void ArmCombatWatch()
    {
        _self = this;
        _services.CombatEvents.CombatEventOccurred += OnCombatEvent;
        try
        {
            _harmony = _services.Harmony.Create("freecamprobe");
            var post = new HarmonyMethod(typeof(FreeCamProbe).GetMethod(nameof(CombatPostfix), BindingFlags.NonPublic | BindingFlags.Static));
            foreach (var name in new[] { "SetLocalCombatData", "SetLocalAttrInBattleShow" })
            {
                var m = AccessTools.Method(typeof(EntityAttrExtensions), name);
                if (m == null) { Log($"COMBAT hook {name}: not found"); continue; }
                _harmony.Patch(m, postfix: post);
                Log($"COMBAT hook EntityAttrExtensions.{name} armed");
            }
        }
        catch (Exception ex) { Log($"COMBAT hook FAILED {ex.GetType().Name}: {ex.Message}"); }
    }

    private void DisarmCombatWatch()
    {
        _services.CombatEvents.CombatEventOccurred -= OnCombatEvent;
        try { _harmony?.UnpatchSelf(); } catch { }
        _self = null;
    }

    private static void CombatPostfix(MethodBase __originalMethod, object[] __args)
    {
        var p = _self;
        if (p == null) return;
        try
        {
            if (__originalMethod.Name == "SetLocalCombatData") p._setLocalCombatHits++; else p._setInBattleShowHits++;
            var args = __args == null ? "" : string.Join(",", Array.ConvertAll(__args, a => a is ZEntity e ? $"ent{e.Uuid}" : a?.ToString() ?? "null"));
            p.Log($"COMBAT FIRED {__originalMethod.Name}({args})");
        }
        catch { }
    }

    private void OnCombatEvent(CombatEvent ev)
    {
        if (ev is not CombatEvent.EntityAttributesChanged ac) return;
        // May run off the main thread: compare against the uuid cached on the main thread, never call the game here.
        var self = _selfUuid;
        if (self == 0 || ac.TargetId.Value != self) return;
        _selfAttrEvents++;
        foreach (var a in ac.Attrs)
        {
            _selfAttrIds.Add(a.AttrId);
            if (a.AttrId is AttrCombatState or AttrCombatStateTime && _combatAttrSeen.Count < 40)
            {
                var line = $"t={ac.TimestampMs} attr{a.AttrId}={a.Value}";
                _combatAttrSeen.Add(line);
                Log($"COMBAT wire attr (framework EntityAttributesChanged) {line}");
            }
        }
    }

    private IEnumerator StepCombatFlag()
    {
        var e = SelfEntity();
        if (e == null) { Log("COMBAT no self entity"); yield break; }
        Try("COMBAT C# reads", () => Log(
            $"COMBAT C# GetLuaIsInCombat={e.GetLuaIsInCombat()} GetLuaLocalAttrInBattleShow={e.GetLuaLocalAttrInBattleShow()} " +
            $"GetIsInCombat={EntityAttrExtensions.GetIsInCombat(e)} GetLocalAttrInBattleShow={EntityAttrExtensions.GetLocalAttrInBattleShow(e)} " +
            $"enterTime={EntityAttrExtensions.GetEnterCombatTime(e)} exitTime={EntityAttrExtensions.GetExitCombatTime(e)}"));
        Log("COMBAT Lua: " + Lua(
            "local p = Z.EntityMgr.PlayerEnt; local cs = Z.PbAttrEnum('AttrCombatState'); local ib = Z.PbAttrEnum('AttrInBattleShow'); " +
            "local ct = Z.PbAttrEnum('AttrCombatStateTime'); " +
            "return 'AttrCombatState id=' .. tostring(cs) .. ' value=' .. tostring(p:GetLuaAttr(cs).Value) .. " +
            "' AttrInBattleShow id=' .. tostring(ib) .. ' AttrCombatStateTime id=' .. tostring(ct) .. ' value=' .. tostring(p:GetLuaAttr(ct).Value) .. " +
            "' GetLuaIsInCombat=' .. tostring(p:GetLuaIsInCombat())"));
        LogCombatSummary();
        Log("COMBAT would hook (event-driven, no polling): primary = framework ICombatEvents EntityAttributesChanged for the local " +
            "player filtered on attr 104 (AttrCombatState) [if the summary shows 104 arrives]; alternative = Harmony postfix on " +
            "EntityAttrExtensions.SetLocalCombatData(ZEntity,bool,long) (local enter/exit setter) [if it FIRES on combat]; Lua-side twin = " +
            "BindEntityLuaAttrWatcher({AttrCombatState}, PlayerEnt) as fighterbtns_view.lua does.");
    }

    private void LogCombatSummary() =>
        Log($"COMBAT summary: selfAttrEvents={_selfAttrEvents} distinctSelfAttrIds={_selfAttrIds.Count} " +
            $"has104={_selfAttrIds.Contains(AttrCombatState)} has114={_selfAttrIds.Contains(AttrCombatStateTime)} " +
            $"SetLocalCombatData hits={_setLocalCombatHits} SetLocalAttrInBattleShow hits={_setInBattleShowHits} " +
            $"ids=[{string.Join(",", _selfAttrIds.OrderBy(i => i).Take(80))}] combatAttrLines=[{string.Join(" ; ", _combatAttrSeen.Take(10))}]");
}
