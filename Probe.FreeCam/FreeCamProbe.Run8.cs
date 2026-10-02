using System;
using System.Collections;
using System.Linq;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run 8 (2026-10-02): verifies the framework freeze fixes (feat/posing 57147d1+: the AnimCompBase.set_Speed gate, the
/// re-apply fix, the own-mount exclusion) on the TEST character through the REAL <c>ISceneFreeze</c>. No hooks of its own
/// (run 7's hook set is never armed here). Steps: restore the character left by run 7 (AutoBattle setting OFF, back to
/// Asterleeds) → town freeze (players / NPCs frozen, self not) → mount: rider↔mount link reads, freeze while mounted,
/// mount up mid-freeze → monsters: teleport near monsters, auto-battle, 15 s freeze → return home, AutoBattle OFF.
/// The framework's own diagnostics lines (<c>[FreeCam] freeze on/held/never frozen</c>) are the verdict; the run-7 sampler
/// (<c>R7S</c>/<c>R7SUM</c>, labelled c81..c84) is the per-entity evidence.
/// </summary>
public sealed partial class FreeCamProbe
{
    private static readonly (int Scene, int Tp)[] R8Fields = { (7, 50707), (7, 50704), (7, 50001), (95, 50731) };

    private IEnumerator StepRestore8()
    {
        var r = Lua("local s=Z.VMMgr.GetVM('setting'); local prev=tostring(s.Get(E.SettingID.AutoBattle)); " +
                    "s.Set(E.SettingID.AutoBattle, false); Z.EntityMgr.PlayerEnt:SetLuaAttr(Z.LocalAttr.EAutoBattleSwitch, false); " +
                    "Panda.ZGame.ZBattleUtils.StopPlayerAIBattle(); Z.EventMgr:Dispatch(Z.ConstValue.AutoBattleChange, false); " +
                    "return 'prev='..prev..' now='..tostring(s.Get(E.SettingID.AutoBattle))..' scene='..tostring(Z.StageMgr.GetCurrentSceneId())");
        Log($"R8 RESTORE autobattle setting: {r}");
        _r7AutoBattlePrev = "false";
        if (r.Contains("scene=8")) { Log("R8 RESTORE already in Asterleeds"); yield break; }
        yield return R8GoHome("restore");
    }

    /// <summary>Run 8a: <c>AsyncUserTp(8, 50801)</c> changed no scene twice. Logs which Asterleeds points are unlocked,
    /// tries the map VM on the first unlocked one, then the bridge call it wraps (<c>ClientReqSwitchSceneByTransfer</c>).</summary>
    private IEnumerator R8GoHome(string why)
    {
        var unlocked = Lua("local vm=Z.VMMgr.GetVM('map'); local o={}; for id=50801,50811 do " +
                           "o[#o+1]=tostring(id)..'='..tostring(vm.CheckTransferPointUnlock(id)) end; return table.concat(o,',')");
        Log($"R8 HOME unlock: {unlocked}");
        var tp = Enumerable.Range(50801, 11).FirstOrDefault(id => unlocked.Contains($"{id}=true"));
        if (tp == 0) tp = R7HomeTp;
        var epoch = _sceneEpoch;
        yield return R7Teleport(R7HomeScene, tp, $"home ({why}) via map vm");
        if (epoch != _sceneEpoch) yield break;
        var epoch2 = _sceneEpoch;
        Log($"R8 HOME bridge: {Lua($"Z.LuaBridge.ClientReqSwitchSceneByTransfer({R7HomeScene}, {tp}); return 'sent'")}");
        var until = Time.realtimeSinceStartup + 45f;
        while (epoch2 == _sceneEpoch && Time.realtimeSinceStartup < until) yield return Wait(0.5f);
        if (epoch2 == _sceneEpoch) { Log("R8 HOME bridge: no scene change within 45 s"); yield break; }
        yield return Wait(10f);
        Log($"R8 HOME arrived scene={Lua("return tostring(Z.StageMgr.GetCurrentSceneId())")} census {R7Census()}");
    }

    private IEnumerator StepTown8()
    {
        var epoch = _sceneEpoch;
        Log($"R8 town census {R7Census()}");
        _r7Cycle = 81;
        _r7FreezeSeconds = 8f;
        _r8Inject = true;
        yield return R7FreezeCycle(epoch);
        _r8Inject = false;
        R8AfterInject();
    }

    // ---- gate check: a set_Speed write through the native setter (what a game write is) on frozen entities ----------

    private bool _r8Inject;
    private readonly System.Collections.Generic.List<(long Uuid, string Kind, float Before)> _r8Injected = new();

    /// <summary>Called from the run-7 sampler's frozen window (~1 s in): writes <c>AnimComp.Speed = 1.25</c> on up to two
    /// frozen monsters/NPCs and on self, through the interop setter (il2cpp_runtime_invoke → the patched native
    /// <c>set_Speed</c>, the same entry a game write takes), then reads it back. Expected with the gate: frozen → 0.00,
    /// self → 1.25 then put back.</summary>
    private void R8InjectNow()
    {
        _r8Inject = false;
        _r8Injected.Clear();
        foreach (var r in _r7Rows.Values.Where(r => r.Kind.StartsWith("MonsterEnt") || r.Kind.StartsWith("NpcEnt") || r.Kind.StartsWith("CharEnt")).Take(6))
        {
            var isSelf = r.Uuid == _selfUuid;
            if (!isSelf && _r8Injected.Count(x => x.Uuid != _selfUuid) >= 3) continue;
            var ac = LiveModel(EntByUuid(r.Uuid))?.AnimComp;
            if (ac == null) continue;
            var before = ac.Speed;
            ac.Speed = 1.25f;
            var after = ac.Speed;
            _r8Injected.Add((r.Uuid, r.Kind, before));
            Log($"R8 INJECT f{R7F()} {r.Kind}:{r.Uuid} drawn before={before:F2} wrote=1.25 readback={after:F2} " +
                $"{(isSelf ? (after > 1.2f ? "PASS-THROUGH (self, expected)" : "SUBSTITUTED (self: WRONG)") : (after <= 0.001f ? "SUBSTITUTED (expected)" : "NOT SUBSTITUTED (gate missed)"))}");
            if (isSelf) ac.Speed = before;   // put self back
        }
    }

    /// <summary>After the unfreeze: the injected frozen entities must resume at the latest written speed (1.25), then the
    /// probe puts their pre-freeze speed back.</summary>
    private void R8AfterInject()
    {
        foreach (var (uuid, kind, _) in _r8Injected.Where(x => x.Uuid != _selfUuid))
        {
            var ac = LiveModel(EntByUuid(uuid))?.AnimComp;
            if (ac == null) { Log($"R8 INJECT-RESTORE {kind}:{uuid} gone"); continue; }
            var now = ac.Speed;
            Log($"R8 INJECT-RESTORE {kind}:{uuid} drawn after unfreeze={now:F2} {(Math.Abs(now - 1.25f) < 0.01f ? "RESTORED-LATEST (expected)" : "other")}");
            ac.Speed = 1f;
        }
    }

    private IEnumerator StepMount8()
    {
        var epoch = _sceneEpoch;
        var equip = Lua("return tostring(Z.VMMgr.GetVM('vehicle').IsHaveVehicleEquip())");
        Log($"R8 MOUNT has vehicle equipped: {equip}; link before: {R8Link()}");
        if (equip != "ok true") { Log("R8 MOUNT skipped: no mount equipped"); yield break; }
        yield return R8Ride("mount up");
        if (Aborted(epoch)) yield break;
        Log($"R8 MOUNT link mounted: {R8Link()}");
        _r7Cycle = 82;
        _r7FreezeSeconds = 6f;
        yield return R7FreezeCycle(epoch);   // freeze while mounted: the framework must exclude the ridden mount
        yield return R8Ride("dismount");
        Log($"R8 MOUNT link dismounted: {R8Link()}");
        yield return Wait(2f);
        yield return R8MountMidFreeze(epoch);
        yield return R8Ride("dismount");
    }

    private IEnumerator R8Ride(string why)
    {
        Log($"R8 RIDE {why}: {Lua("Z.VMMgr.GetVM('vehicle').TakeRide(); return 'sent'")}");
        yield return Wait(5f);
        Log($"R8 RIDE {why} -> {R8Link()}");
    }

    /// <summary>The rider ↔ mount link as the framework reads it: self <c>GetAttrRideUuid</c> / <c>GetAttrRideId</c> /
    /// Lua riding id + stage, and per mount within 15 m its uuid, <c>GetVehicleController</c>, kind and summon flag.</summary>
    private string R8Link()
    {
        var me = SelfEntity();
        var self = me == null ? "none" :
            $"self={_selfUuid} rideUuid={Safe(() => EntityAttrExtensions.GetAttrRideUuid(me).ToString())} " +
            $"rideId={Safe(() => EntityAttrExtensions.GetAttrRideId(me).ToString())} " +
            $"lua={Lua("local p=Z.EntityMgr.PlayerEnt; return tostring(p:GetLuaRidingId())..'/stage'..tostring(p:GetLuaRideStage())")}";
        var mounts = AllEntities(15f).Where(x => x.Kind == "VehicleEnt" || x.LuaType == 19).Select(x =>
        {
            var e = EntByUuid(x.Uuid);
            return $"{x.Kind}:{x.Uuid}@{x.Dist:F1}m ctrl={Safe(() => EntityAttrExtensions.GetVehicleController(e!).ToString())} " +
                   $"rideUuid={Safe(() => EntityAttrExtensions.GetAttrRideUuid(e!).ToString())} summon={x.Summon}";
        });
        return $"{self} mounts=[{string.Join(" | ", mounts)}]";
    }

    /// <summary>Freeze first, then mount up: the new mount appears mid-freeze and must never be frozen or held.</summary>
    private IEnumerator R8MountMidFreeze(int epoch)
    {
        _r7Cycle = 83;
        _r7Rows.Clear();
        R7RefreshTracked();
        _r7Phase = "frozen";
        _r7Frame0 = Time.frameCount;
        ProbeTicks.LateTick = R7Sample;
        Arm("r8.sampler", () => { ProbeTicks.LateTick = null; _r7Phase = "off"; });
        IDisposable? token = null;
        try { token = _services.SceneFreeze.Freeze(); }
        catch (Exception ex) { Log($"R8C83 Freeze() THREW {ex.GetType().Name}: {ex.Message}"); }
        Arm("r8.freeze", () => token?.Dispose());
        Log($"R8C83 FREEZE applied (not mounted) isFrozen={_services.SceneFreeze.IsFrozen}; mounting up mid-freeze");
        yield return Wait(1f);
        Log($"R8 RIDE mid-freeze: {Lua("Z.VMMgr.GetVM('vehicle').TakeRide(); return 'sent'")}");
        var t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 7f && !Aborted(epoch))
        {
            if (Time.frameCount % 30 == 0) R7RefreshTracked();
            yield return null;
        }
        Log($"R8C83 mid-freeze link: {R8Link()}");
        R7LogCycleSummary(83);
        Release("r8.freeze");
        yield return Wait(1f);
        Release("r8.sampler");
    }

    private IEnumerator StepCombat8()
    {
        var unlocked = Lua("local vm=Z.VMMgr.GetVM('map'); local o={}; for _,id in ipairs({" +
                           string.Join(",", R8Fields.Select(f => f.Tp)) + "}) do if vm.CheckTransferPointUnlock(id) then o[#o+1]=tostring(id) end end; return table.concat(o,',')");
        Log($"R8 COMBAT unlocked points: {unlocked}");
        var pick = R8Fields.FirstOrDefault(f => unlocked.Contains(f.Tp.ToString()));
        if (pick.Tp == 0) { Log("R8 COMBAT no field point unlocked"); yield break; }
        yield return R7Teleport(pick.Scene, pick.Tp, "monsters");
        var epoch = _sceneEpoch;
        yield return R7WalkToMonster(epoch);
        R7AutoBattle(true);
        var start = Time.realtimeSinceStartup;
        var engaged = false;
        while (Time.realtimeSinceStartup - start < 75f && !Aborted(epoch))
        {
            yield return Wait(5f);
            Log($"R8 wait-combat +{Time.realtimeSinceStartup - start:F0}s {R7Census()}");
            if (R7Combat(SelfEntity()) && R7Monsters(R7Range).Count > 0) { engaged = true; break; }
        }
        Log($"R8 combat {(engaged ? "ENGAGED" : "NOT ENGAGED (freezing whatever is near anyway)")}");
        if (Aborted(epoch)) yield break;
        _r7Cycle = 84;
        _r7FreezeSeconds = 16f;
        yield return R7FreezeCycle(epoch);
        R7AutoBattle(false);
    }

    private IEnumerator StepReturn8()
    {
        R7AutoBattle(false);
        var r = Lua("local s=Z.VMMgr.GetVM('setting'); s.Set(E.SettingID.AutoBattle, false); " +
                    "Z.EntityMgr.PlayerEnt:SetLuaAttr(Z.LocalAttr.EAutoBattleSwitch, false); return 'setting='..tostring(s.Get(E.SettingID.AutoBattle))");
        Log($"R8 RETURN autobattle {r}");
        if (Lua("return tostring(Z.StageMgr.GetCurrentSceneId())") == "ok 8") yield break;
        yield return R8GoHome("return");
        Log($"R8 RETURN done scene={Lua("return tostring(Z.StageMgr.GetCurrentSceneId())")}");
    }
}
