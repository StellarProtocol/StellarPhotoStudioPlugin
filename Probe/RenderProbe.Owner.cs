using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Panda.ZGame;
using Stellar.Abstractions.Domain;
using UnityEngine;

namespace Stellar.PhotoStudioProbe;

/// <summary>
/// Automates the formerly owner-driven rows: camera/selfie mode entered through the game's OWN UI entry points
/// (camerasys VM + the view's own toggles/close handler — the code paths the player's clicks run), and a measured
/// other-players hide with projected on-screen positions.
/// </summary>
public sealed partial class RenderProbe
{
    private string Lua(string body)
    {
        if (!_services.Lua.Ready) return "LUA NOT READY";
        _services.Lua.DoString(
            "_G.__pp_out = nil local _ppOk, _ppR = pcall(function() " + body + " end) " +
            "_G.__pp_out = _ppOk and ('ok ' .. tostring(_ppR)) or ('ERR ' .. tostring(_ppR))");
        return _services.Lua.ReadGlobalString("__pp_out") ?? "null";
    }

    private void LogModules()
    {
        try
        {
            var mods = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .Where(m => m.ModuleName.StartsWith("dxgi", StringComparison.OrdinalIgnoreCase)
                            || m.ModuleName.StartsWith("d3d11", StringComparison.OrdinalIgnoreCase)
                            || m.ModuleName.IndexOf("reshade", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(m => $"{m.ModuleName}={m.FileName}");
            Log($"MODULES {string.Join(" | ", mods)}");
        }
        catch (Exception ex) { Log($"MODULES FAILED {ex.Message}"); }
    }

    // ---------------- photo mode via the player's path ----------------

    private IEnumerator StepPhotoModePlayerPath()
    {
        Log("PHOTO open camera: " + Lua("local vm = Z.VMMgr.GetVM('camerasys'); vm.GotoMainUIAndopenCameraView(); return tostring(Z.UIMgr:IsActive('camerasys_main_pc'))"));
        yield return Wait(3f);
        Log("PHOTO camera view active=" + Lua("return tostring(Z.UIMgr:IsActive('camerasys_main_pc'))"));
        yield return Snap("07a_camera_mode");
        Log("PHOTO selfie toggle: " + Lua("local v = Z.UIMgr:GetView('camerasys_main_pc'); v.uiBinder.tog_self.isOn = true; return tostring(v.cameraData_.CameraPatternType)"));
        yield return Wait(3f);
        Log("PHOTO pattern=" + Lua("local v = Z.UIMgr:GetView('camerasys_main_pc'); return tostring(v.cameraData_.CameraPatternType)"));
        yield return Snap("07b_selfie");
        Log("PHOTO default toggle: " + Lua("local v = Z.UIMgr:GetView('camerasys_main_pc'); v.uiBinder.tog_default.isOn = true; return tostring(v.cameraData_.CameraPatternType)"));
        yield return Wait(3f);
        yield return Snap("07c_back_default");
        var closeRes = ClickButton(b => PathOf(b.transform).Contains("camerasys_main_pc") && b.gameObject.name.Contains("btn_close"), "btn_close");
        Log("PHOTO close: " + closeRes);
        if (closeRes.Contains("NOT FOUND"))
            Log("PHOTO close fallback (the btn_close handler body, view:requestCloseView): " + Lua("local v = Z.UIMgr:GetView('camerasys_main_pc'); v:requestCloseView(); return 'requested'"));
        yield return Wait(2f);
        Log("PHOTO exit-confirm dialog: " + ClickButton(b => TextOf(b) == "Confirm", "Confirm"));
        yield return Wait(3f);
        Log("PHOTO camera view active after close=" + Lua("return tostring(Z.UIMgr:IsActive('camerasys_main_pc'))"));
        yield return Snap("07d_closed");
    }

    private static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (var c = t; c != null; c = c.parent) parts.Add(c.gameObject.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string TextOf(Panda.ZUi.ZButton b)
    {
        var tmp = b.GetComponentInChildren<TMPro.TMP_Text>();
        return tmp == null ? "" : (tmp.text ?? "").Trim();
    }

    /// <summary>Click a live game button the way AutoNav does (ZButton.invokeClickEvent — the button's own handler).</summary>
    private string ClickButton(Func<Panda.ZUi.ZButton, bool> match, string what)
    {
        var all = UnityEngine.Object.FindObjectsOfType<Panda.ZUi.ZButton>();
        Panda.ZUi.ZButton? hit = null;
        foreach (var b in all)
        {
            if (!b.gameObject.activeInHierarchy) continue;
            if (match(b)) { hit = b; break; }
        }
        if (hit == null)
        {
            var sample = all.Where(b => b.gameObject.activeInHierarchy).Take(40).Select(b => $"{b.gameObject.name}('{TextOf(b)}')");
            return $"'{what}' NOT FOUND among {all.Length} ZButtons; active sample: {string.Join(", ", sample)}";
        }
        var path = PathOf(hit.transform);
        hit.invokeClickEvent();
        return $"clicked '{what}' at {path}";
    }

    // ---------------- other players ----------------

    private sealed record Seen(long CharId, Vector3 Pos, Vector3 Viewport, bool OnScreen);

    private List<Seen> CountPlayers(out string selfNote)
    {
        var seen = new List<Seen>();
        var em = ZEntityMgr.Instance;
        var cam = CameraManager.Instance?.MainCamera ?? Camera.main;
        var ids = em?.CharIdList;
        selfNote = $"playerUuid={em?.PlayerUuid} charIdList.Count={ids?.Count}";
        if (em == null || ids == null) return seen;
        var self = em.GetEntity(em.PlayerUuid);
        for (var i = 0; i < ids.Count; i++)
        {
            var cid = ids[i];
            var ent = em.GetCharEntity(cid);
            if (ent == null || (self != null && ent.Uuid == self.Uuid)) continue;
            var model = ent.Model;
            if (model == null) { seen.Add(new Seen(cid, Vector3.zero, Vector3.zero, false)); continue; }
            var w = model.GetChestPosition();
            var vp = cam != null ? cam.WorldToViewportPoint(w) : Vector3.zero;
            var on = vp.z > 0 && vp.x is > 0.02f and < 0.98f && vp.y is > 0.02f and < 0.98f;
            seen.Add(new Seen(cid, w, vp, on));
        }
        return seen;
    }

    /// <summary>Live renderers (enabled, active, not forced off) within 2.5 m of each other player's chest.</summary>
    private static string RendererCount(List<Seen> s)
    {
        var smr = UnityEngine.Object.FindObjectsOfType<Renderer>();
        int live = 0, total = 0, players = 0;
        foreach (var p in s)
        {
            if (p.Pos == Vector3.zero) continue;
            players++;
            foreach (var r in smr)
            {
                if ((r.bounds.center - p.Pos).sqrMagnitude > 6.25f) continue;
                total++;
                if (r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff) live++;
            }
        }
        return $"renderersNearOtherPlayers live={live}/{total} over {players} player(s)";
    }

    private string Describe(List<Seen> s) =>
        $"nearby={s.Count} onScreen={s.Count(x => x.OnScreen)} [" +
        string.Join("; ", s.Take(12).Select(x => $"{x.CharId}@vp({x.Viewport.x:F2},{x.Viewport.y:F2},{x.Viewport.z:F1}){(x.OnScreen ? "*" : "")}")) + "]";

    private IEnumerator StepOthersMeasured()
    {
        var cam = CameraManager.Instance?.MainCamera;
        List<Seen> s = CountPlayers(out var note);
        Log($"PLAYERS {note}");
        var selfEnt = ZEntityMgr.Instance?.GetEntity(ZEntityMgr.Instance.PlayerUuid);
        if (cam != null && selfEnt?.Model != null)
            Log($"PLAYERS self chest={selfEnt.Model.GetChestPosition()} vp={cam.WorldToViewportPoint(selfEnt.Model.GetChestPosition())} camera={cam.transform.position}");
        for (var i = 0; i < 3 && s.Count(x => x.OnScreen) == 0; i++)
        {
            yield return Wait(5f);
            s = CountPlayers(out _);
        }
        // Nobody in frame: turn the orbit camera toward each nearby player in turn (what a player does with the mouse).
        foreach (var target in s.Where(x => x.Pos != Vector3.zero).OrderBy(x => (x.Pos - cam!.transform.position).sqrMagnitude).ToList())
        {
            if (s.Count(x => x.OnScreen) > 0 || selfEnt?.Model == null) break;
            var d = target.Pos - selfEnt.Model.GetChestPosition();
            var heading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            try
            {
                var fl = CameraManager.Instance.GetDefaultCM();
                var ax = fl.m_XAxis;
                var before = ax.Value;
                ax.Value = heading;
                fl.m_XAxis = ax;
                Log($"PLAYERS turn camera toward {target.CharId} dist={d.magnitude:F1}m: FreeLook X {before:F1} -> {heading:F1}");
            }
            catch (Exception ex) { Log($"PLAYERS turn camera FAILED {ex.GetType().Name}: {ex.Message}"); break; }
            yield return Wait(1.5f);
            s = CountPlayers(out _);
            Log($"PLAYERS after turn: {Describe(s)} {RendererCount(s)}");
        }
        Log($"PLAYERS before hide: {Describe(s)} {RendererCount(s)}");
        yield return Snap("08a_others_before");
        var cf = CameraFrameCtrl.Instance;
        cf.SetEntityShow(11, false);
        yield return Wait(0.5f);
        Log($"PLAYERS after SetEntityShow(11,false): {Describe(CountPlayers(out _))} {RendererCount(s)}");
        yield return Snap("08b_others_hidden11");
        foreach (var t in new[] { 2, 3, 4, 5, 6 }) cf.SetEntityShow(t, false);
        yield return Wait(0.5f);
        Log($"PLAYERS after SetEntityShow(2-6,false): {RendererCount(s)}");
        yield return Snap("08c_others_hidden11_2to6");
        foreach (var t in new[] { 2, 3, 4, 5, 6, 11 }) cf.SetEntityShow(t, true);
        yield return Wait(0.5f);
        Log($"PLAYERS restored: {Describe(CountPlayers(out _))} {RendererCount(s)}");
        yield return Snap("08d_others_restored");
        Log("PLAYERS viewports for diff: " + string.Join(";", s.Where(x => x.OnScreen).Select(x => $"{x.Viewport.x:F3},{x.Viewport.y:F3}")));
    }

    private static EntityId SelfEntity(string note)
    {
        var parts = note.StartsWith("ok ") ? note.Substring(3).Split('|') : Array.Empty<string>();
        return parts.Length > 0 && long.TryParse(parts[0], out var uuid) ? new EntityId(uuid) : default;
    }
}
