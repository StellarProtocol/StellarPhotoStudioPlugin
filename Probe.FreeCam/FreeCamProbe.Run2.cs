using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Panda.ZGame;
using UnityEngine;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// Run-2 shared helpers (spec § 9 follow-ups A-G): emote driving, model-kind mapping (ECS vs GameObject models — the
/// reason run 1 matched no scene roots), other-character regions and an end-of-frame wait.
/// </summary>
public sealed partial class FreeCamProbe
{
    /// <summary>Plays an emote through the emote wheel's own VM path (no packet built here). Returns the Lua result.</summary>
    private string PlayEmote(int id) =>
        Lua($"Z.VMMgr.GetVM('expression').PlayAction({id}, true, false); return 'played {id}; actorState=' .. tostring(Z.EntityMgr.PlayerEnt:GetLuaLocalAttrState())");

    private string ActorState() => Lua("return tostring(Z.EntityMgr.PlayerEnt:GetLuaLocalAttrState())");

    private bool EmoteAllowed(int id) => Lua($"return tostring(Z.VMMgr.GetVM('expression').CheckEmoteCondition({id}, false))") == "ok true";

    /// <summary>Waits (bounded 5 s) until the local actor state returns to 0 (emote over).</summary>
    private IEnumerator WaitEmoteEnd(int epoch)
    {
        var until = Time.realtimeSinceStartup + 5f;
        while (Time.realtimeSinceStartup < until && !Aborted(epoch))
        {
            if (ActorState() == "ok 0") break;
            yield return Wait(0.2f);
        }
        yield return Wait(0.4f);   // let the idle blend settle
    }

    private static IEnumerator EndOfFrame()
    {
        yield return new WaitForEndOfFrame();
    }

    private RectInt? CharRegion(Char c, float halfW = 0.06f, float halfH = 0.12f)
    {
        var m = LiveModel(CharEntity(c.CharId, c.IsSelf));
        var cam = MainCam();
        if (m == null || cam == null) return null;
        var vp = cam.WorldToViewportPoint(m.GetChestPosition());
        if (vp.z <= 0f || vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.95f) return null;   // off screen
        return RegionAround(cam, m.GetChestPosition(), halfW, halfH);
    }

    /// <summary>The model's Unity GameObject (GameObject-backed models only; ECS models have none).</summary>
    private static GameObject? ModelGo(ZModel m)
    {
        var mg = m.ModelGoComp;
        if (mg == null) return null;
        var n = mg.TryCast<ModelGoComp>();
        if (n != null) return n.UGo;
        var zgo = mg.ModelGameObject?.TryCast<ZModelGameObject>();
        return zgo?.Go;
    }

    private static string ModelKind(ZModel m)
    {
        try
        {
            var mg = m.ModelGoComp;
            var ac = m.AnimComp;
            var go = ModelGo(m);
            var anims = go == null ? 0 : go.GetComponentsInChildren<Animator>(true).Length;
            var ecsUid = mg?.TryCast<ECSModelGoComp>()?.EcsUID;
            return $"goComp={(mg == null ? "null" : mg.GetIl2CppType().Name)} animComp={(ac == null ? "null" : ac.GetIl2CppType().Name)} " +
                   $"ugo={(go == null ? "null" : go.name)} animators={anims} ecsUid={(ecsUid?.ToString() ?? "-")} goCompPos={(mg == null ? "-" : V(mg.Position))} attrPos={V(m.GetAttrGoPosition())}";
        }
        catch (Exception ex) { return $"kind err {ex.GetType().Name}: {ex.Message}"; }
    }

    private void LogModelKinds(string tag, List<Char> chars)
    {
        foreach (var c in chars.Take(8))
        {
            var m = LiveModel(CharEntity(c.CharId, c.IsSelf));
            Log($"{tag} model {(c.IsSelf ? "self" : c.CharId.ToString())}@{c.Dist:F1}m {(m == null ? "gone" : ModelKind(m))}");
        }
    }

    /// <summary>"frozen" heuristic for a t1/t2 pair: changed share below max(3 %, 1.5 × static noise).</summary>
    private static string Verdict(float changedPct, float noisePct) =>
        changedPct < 0 ? "n/a" : changedPct <= Math.Max(3f, noisePct * 1.5f) ? "FROZEN" : "MOVING";
}
