using System;
using System.Collections;
using Panda;
using Panda.ZGame;
using UnityEngine;
using WriteNotify = Panda.ZGame.Pure.WriteNotify;

namespace Stellar.PhotoStudio.FreeCamProbe;

public sealed partial class FreeCamProbe
{
    private PoseTarget? _r5Npc;

    private string NpcState(ZModel? m) =>
        m == null ? "gone" : $"actionInfo[{ActionInfo(m)}] {YawText(m)} pos={Safe(() => V(m.GetAttrGoPosition()))} rb[{EmoRb(m)}]";

    /// <summary>The camera Partner path (camera_member_vm GenerateModelAsync + camerasys_vm OpenGroupPhoto): generate a NEW
    /// model from NpcTable.ModelID at the scene NPC's position/rotation, copy its base idle, hide the scene NPC.</summary>
    private IEnumerator GenerateNpcModel(ZModel src, int modelId, bool cameraScene, Action<ZModel?, string> done)
    {
        ZModel? gen = null;
        var loaded = false;
        string err = "";
        var applied = "n/a";
        var pos = src.GetAttrGoPosition();
        var rot = EntityAttrExtensions.GetAttrGoRotation(src);
        var t0 = Time.realtimeSinceStartup;
        Action<ZModel> pre = m =>
        {
            gen = m;
            try
            {
                var p = pos; var r = rot;
                EntityAttrExtensions.SetAttrGoPosition(m, ref p, WriteNotify.Now);
                EntityAttrExtensions.SetAttrGoRotation(m, ref r);
                m.SetLuaAttrModelShowLoadType(EShowLoadType.ENone, EShowLoadType.ENone, EShowLoadType.ENone, EShowLoadType.EAll);
                applied = "pre:" + LuaAsyncBridge.ApplyModelBaseIdleByLua(src, m);
            }
            catch (Exception ex) { err += $"pre {ex.GetType().Name}: {ex.Message} "; }
        };
        Action<ZModel> onLoad = m =>
        {
            loaded = true;
            gen ??= m;
            try { applied += " load:" + LuaAsyncBridge.ApplyModelBaseIdleByLua(src, m); }
            catch (Exception ex) { err += $"load {ex.GetType().Name}: {ex.Message} "; }
        };
        Action<Il2CppSystem.Exception> onEx = ex => err += "onException " + ex?.Message;
        try { LuaAsyncBridge.GenerateNormalModelAsyncByLua(modelId, pre, onLoad, onEx, cameraScene); }
        catch (Exception ex) { done(null, $"call threw {ex.GetType().Name}: {ex.Message}"); yield break; }
        while (!loaded && err.Length == 0 && Time.realtimeSinceStartup - t0 < 12f) yield return null;
        var ms = (Time.realtimeSinceStartup - t0) * 1000f;
        done(gen, $"cameraScene={cameraScene} loaded={loaded} in {ms:F0} ms gen={(gen != null)} applyBaseIdle={applied} err='{err}' " +
                  $"genPos={(gen == null ? "-" : Safe(() => V(gen.GetAttrGoPosition())))} genVisPos={(gen == null ? "-" : Safe(() => V(gen.ModelGoComp.Position)))} kind[{(gen == null ? "-" : Safe(() => ModelKind(gen)))}]");
    }

    private void RecycleGen(ZModel? m)
    {
        if (m == null) return;
        ZModelManager.Instance.RecycleModelByLua(m);
    }

    private IEnumerator StepNpcClone5()
    {
        var epoch = _sceneEpoch;
        var npc = NearestNpc();
        if (npc == null) { Log("R5 npc: none within 25 m"); _currentOutcome = "skipped"; yield break; }
        _r5Npc = npc;
        var e = EntByUuid(npc.Uuid);
        var src = LiveModel(e);
        if (e == null || src == null) { Log("R5 npc: no model"); yield break; }
        var info = Lua($"local e = Z.EntityMgr:GetEntity({npc.Uuid}); local cfg = e:GetLuaAttr(Z.PbAttrEnum('AttrId')).Value; " +
                       "local r = Z.TableMgr.GetTable('NpcTableMgr').GetRow(cfg); return tostring(cfg) .. '|' .. tostring(r and r.ModelID) .. '|' .. tostring(r and r.Name)");
        Log($"R5 npc {npc.Tag}: cfg|modelId|name = {info}; real pre {NpcState(src)}");
        var parts = info.Replace("ok ", "").Split('|');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var modelId) || modelId <= 0) { Log("R5 npc: no ModelID"); yield break; }
        var srcTarget = new PoseTarget { Tag = "npcreal", Uuid = npc.Uuid };
        yield return Aim(srcTarget, false);
        var region = _r4Region;
        var before0 = Cap("R5_npc_real_before0");
        yield return Wait(1.0f);
        var before1 = Cap("R5_npc_real_before1");
        Log($"R5 npc real idle noise {DiffText(before0, before1)}");
        var preYaw = Yaw(EntityAttrExtensions.GetAttrGoRotation(src));
        var preAction = src.GetLuaAttrActionInfoActionId();

        ZModel? gen = null;
        foreach (var cameraScene in new[] { true, false })
        {
            var mark = SendMark();
            string res = "";
            yield return GenerateNpcModel(src, modelId, cameraScene, (g, r) => { gen = g; res = r; });
            Log($"R5 npc generate {res} SENDS {SendsSince(mark)}");
            if (gen == null) continue;
            var keep = gen;
            Arm("r5.npcgen", () => RecycleGen(keep));
            yield return Wait(1.0f);
            _r4Region = region;
            var both = Cap($"R5_npc_gen{(cameraScene ? "C" : "N")}_bothVisible");
            CameraFrameCtrl.Instance.SetTargetEntityVisible(e, false);
            yield return Wait(0.8f);
            var genOnly = Cap($"R5_npc_gen{(cameraScene ? "C" : "N")}_srcHidden");
            RecycleGen(gen);
            yield return Wait(0.8f);
            var empty = Cap($"R5_npc_gen{(cameraScene ? "C" : "N")}_bothGone");
            Log($"R5 npc gen visibility cameraScene={cameraScene}: genOnly vs real {DiffText(before1, genOnly)}; bothVisible vs real {DiffText(before1, both)}; empty vs real {DiffText(before1, empty)}; empty vs genOnly {DiffText(genOnly, empty)} (real noise above)");
            _releases.Remove("r5.npcgen");
            CameraFrameCtrl.Instance.SetTargetEntityVisible(e, true);
            var visible = Diff(before1, genOnly).ChangedPct < Diff(before1, empty).ChangedPct * 0.5f;
            gen = null;
            if (!visible) { yield return Wait(0.8f); continue; }
            yield return PoseNpcGen(e, src, modelId, cameraScene, before1, epoch);
            break;
        }
        yield return Wait(1.5f);
        src = LiveModel(EntByUuid(npc.Uuid));
        yield return Aim(srcTarget, false);   // same framing as before0/1 (5a captured from the game camera here)
        _r4Region = region;
        var after = Cap("R5_npc_real_after");
        yield return Wait(1.0f);
        var after1 = Cap("R5_npc_real_after1");
        Log($"R5 npc REAL after noise {DiffText(after, after1)}; after1 vs before0 {DiffText(before0, after1)}");
        Release("cam.vcam");
        _r4Vcam = null;
        Log($"R5 npc REAL after generate/pose/recycle: vs before {DiffText(before1, after)} (noise above) preAction={preAction:F0} preYaw={preYaw:F1} now {NpcState(src)}");
        if (Aborted(epoch)) yield break;
    }

    private IEnumerator PoseNpcGen(ZEntity e, ZModel src, int modelId, bool cameraScene, Shot? realIdle, int epoch)
    {
        ZModel? gen = null;
        string res = "";
        yield return GenerateNpcModel(src, modelId, cameraScene, (g, r) => { gen = g; res = r; });
        Log($"R5 npc pose-model generate {res}");
        if (gen == null) yield break;
        var t = new PoseTarget { Tag = "npcgen", Uuid = e.Uuid, Clone = gen };
        var keep = gen;
        CameraFrameCtrl.Instance.SetTargetEntityVisible(e, false);
        t.SourceHidden = true;
        Arm("r5.npcgen", () => { try { RecycleGen(keep); } finally { UnhideSource(t); } });
        yield return Wait(1.0f);
        Log($"R5 npcgen state {NpcState(gen)} look[{Look(gen)}]");
        yield return Aim(t, false);
        var idle = Cap("R5_npcgen_idle");
        Log($"R5 npcgen idle vs real idle {DiffText(realIdle, idle)}");
        yield return PoseAction(t, idle, epoch);
        if (Aborted(epoch)) { Release("r5.npcgen"); yield break; }
        yield return PoseRotate(t, epoch);
        yield return PoseHead(t, epoch);
        yield return PoseEyes(t, epoch);
        yield return FaceHold(t, 303, 403, full: false);
        Release("r5.npcgen");
        Release("cam.vcam");
        _r4Vcam = null;
    }

    /// <summary>Fallback/extra: what does a LIVE NPC need to return to its scene idle after a pose?</summary>
    private IEnumerator StepNpcLiveReset5()
    {
        var epoch = _sceneEpoch;
        var npc = _r5Npc ?? NearestNpc();
        var m = LiveModel(npc == null ? null : EntByUuid(npc.Uuid));
        if (npc == null || m == null) { Log("R5 npc-live: none"); _currentOutcome = "skipped"; yield break; }
        var t = new PoseTarget { Tag = "npclive", Uuid = npc.Uuid };
        yield return Aim(t, false);
        var idle0 = Cap("R5_npclive_idle0");
        yield return Wait(1.0f);
        var idle = Cap("R5_npclive_idle");
        Log($"R5 npclive idle noise {DiffText(idle0, idle)}");
        var preAction = (int)m.GetLuaAttrActionInfoActionId();
        var preTotal = m.GetLuaAttrActionInfoTotalTime();
        Log($"R5 npclive pre {NpcState(m)}");
        // keeper: a hidden generated model that carries the pristine scene base idle (copied BEFORE posing)
        ZModel? keeper = null;
        var info = Lua($"local e = Z.EntityMgr:GetEntity({npc.Uuid}); local cfg = e:GetLuaAttr(Z.PbAttrEnum('AttrId')).Value; return tostring(Z.TableMgr.GetTable('NpcTableMgr').GetRow(cfg).ModelID)");
        if (int.TryParse(info.Replace("ok ", ""), out var keeperModelId))
        {
            string kres = "";
            yield return GenerateNpcModel(m, keeperModelId, true, (g, r) => { keeper = g; kres = r; });
            if (keeper != null)
            {
                var far = m.GetAttrGoPosition() + Vector3.down * 60f;
                EntityAttrExtensions.SetAttrGoPosition(keeper, ref far, WriteNotify.Now);
                var kk = keeper;
                Arm("r5.keeper", () => RecycleGen(kk));
            }
            Log($"R5 npclive keeper {kres}");
        }
        m = ModelOf(t);
        if (m == null) yield break;
        var mark = SendMark();
        Play(t, m, _r4ActionId);
        yield return Wait(1.5f);
        var posed = Cap("R5_npclive_posed");
        Log($"R5 npclive posed vs idle {DiffText(idle, posed)} {NpcState(ModelOf(t))}");
        var attempts = new (string Name, Action<ZModel> Do)[]
        {
            ("r0 SetActionPersistTime(-1)+ResetAction(m,noExitState:true)", mm => { Anim()!.SetActionPersistTime(mm, -1f); Anim()!.ResetAction(mm, true); }),
            ("r3 ResetAction(m)+ApplyModelBaseIdleByLua(keeper,m)", mm => { Anim()!.SetActionPersistTime(mm, -1f); Anim()!.ResetAction(mm, false); Log($"R5 npclive r3 returned {(keeper == null ? "no keeper" : LuaAsyncBridge.ApplyModelBaseIdleByLua(keeper, mm).ToString())}"); }),
        };
        foreach (var (name, act) in attempts)
        {
            m = ModelOf(t);
            if (m == null || Aborted(epoch)) yield break;
            try { act(m); } catch (Exception ex) { Log($"R5 npclive {name} threw {ex.GetType().Name}: {ex.Message}"); continue; }
            yield return Wait(2f);
            var s2 = Cap($"R5_npclive_{name.Substring(0, 2)}_2s");
            yield return Wait(3f);
            var s5 = Cap($"R5_npclive_{name.Substring(0, 2)}_5s");
            Log($"R5 npclive {name}: +2s vs idle {DiffText(idle, s2)}; +5s vs idle {DiffText(idle, s5)}; vs posed {DiffText(posed, s5)} {NpcState(ModelOf(t))}");
        }
        Release("r5.keeper");
        Log($"R5 npclive SENDS window {SendsSince(mark)} preTotal={preTotal:F2}");
    }
}
