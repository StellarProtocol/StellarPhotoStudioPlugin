using System;
using System.Collections;
using System.Text;
using Panda.ZGame;
using Panda.ZInput;

namespace Stellar.PhotoStudio.FreeCamProbe;

/// <summary>
/// § 9 item 2 — input shield. Two candidates, both measured (nothing is injected):
/// (a) <c>PlayerInputController.SetPhotoPlayerMoveShield()</c> — parameterless; recon shows it only raises the Lua
///     event <c>LuaEventDefine.PhotoPlayerMoveShield</c> (no Lua listener exists in the dumped sources), so the probe
///     counts that event through a temporary Lua listener and diffs the controller state + ignore mask.
/// (b) <c>ZIgnoreMgr.SetInputIgnore(mask, bool, EIgnoreMaskSource.EPhoto)</c> — the source-keyed input ignore the
///     game's own camera view uses (<c>camerasys_main_pc_view.lua</c> SetUIViewInputIgnore / IgnoreMgr calls);
///     inverse = the same call with <c>false</c> and the same source.
/// Framework hotkeys: freecamprobe.ping (F6) is logged while the shield is held (presses need a human; auto logs 0).
/// </summary>
public sealed partial class FreeCamProbe
{
    // EInputMask bit indices blocked for the free camera: movement, camera input, combat, interaction.
    private static readonly EInputMask[] ShieldBits =
    {
        EInputMask.Move, EInputMask.Zoom, EInputMask.Rotation, EInputMask.Jump, EInputMask.Rush, EInputMask.NormalAttack,
        EInputMask.ClimbRush, EInputMask.RecoverCamera, EInputMask.Aim, EInputMask.Skill, EInputMask.Flow, EInputMask.Glide,
        EInputMask.AutoMove, EInputMask.LockTarget, EInputMask.QuickUseItem, EInputMask.ChangeWeapon, EInputMask.Interact,
        EInputMask.Dimension, EInputMask.Walk, EInputMask.Run, EInputMask.UseQuestItem, EInputMask.Resonance1,
        EInputMask.Resonance2, EInputMask.RotationX, EInputMask.RotationY, EInputMask.SkillWithWhiteList,
    };

    private IEnumerator StepInputShield()
    {
        var epoch = _sceneEpoch;
        var pic = PlayerInputController.IsCreated ? PlayerInputController.Instance : null;
        var ig = ZIgnoreMgr.IsCreated ? ZIgnoreMgr.Instance : null;
        if (pic == null || ig == null) { Log($"SHIELD prerequisites missing pic={pic != null} ignoreMgr={ig != null}"); yield break; }
        Log("SHIELD state before: " + PicState(pic) + " ignore=" + IgnoreBits(ig) + " photoSrc=" + IgnoreBitsBySource(ig));
        Log("SHIELD lua listener: " + Lua(
            "rawset(_G,'__fcp_shield_n',0); rawset(_G,'__fcp_shield_fn', function(...) rawset(_G,'__fcp_shield_n', (rawget(_G,'__fcp_shield_n') or 0)+1) end); " +
            "Z.EventMgr:Add(Z.ConstValue.Camera.PhotoPlayerMoveShield, rawget(_G,'__fcp_shield_fn')); return 'added ' .. tostring(Z.ConstValue.Camera.PhotoPlayerMoveShield)"));
        Arm("shield.luaListener", () => Lua("Z.EventMgr:Remove(Z.ConstValue.Camera.PhotoPlayerMoveShield, rawget(_G,'__fcp_shield_fn')); return 'removed'"));

        // (a) the game's photo "move shield" call.
        try
        {
            pic.SetPhotoPlayerMoveShield();
            Log("SHIELD (a) SetPhotoPlayerMoveShield() called #1");
        }
        catch (Exception ex) { Log($"SHIELD (a) call FAILED {ex.GetType().Name}: {ex.Message}"); }
        yield return Frames(3);
        Log("SHIELD (a) after #1: " + PicState(pic) + " ignore=" + IgnoreBits(ig) + " luaEvents=" + Lua("return tostring(rawget(_G,'__fcp_shield_n'))"));
        Try("SHIELD (a) #2", () => pic.SetPhotoPlayerMoveShield());
        yield return Frames(3);
        Log("SHIELD (a) after #2 (toggle-back test): " + PicState(pic) + " ignore=" + IgnoreBits(ig) + " luaEvents=" + Lua("return tostring(rawget(_G,'__fcp_shield_n'))"));
        Release("shield.luaListener");

        // (b) source-keyed ignore mask (EPhoto).
        ulong mask = 0;
        foreach (var b in ShieldBits) mask |= 1UL << (int)b;
        var pings0 = _pingCount;
        try
        {
            ig.SetInputIgnore(mask, true, EIgnoreMaskSource.EPhoto);
            Arm("shield.ignore", () => ZIgnoreMgr.Instance?.SetInputIgnore(mask, false, EIgnoreMaskSource.EPhoto));
            Log($"SHIELD (b) SetInputIgnore(mask=0x{mask:X}, true, EPhoto) -> ignore={IgnoreBits(ig)} photoSrc={IgnoreBitsBySource(ig)} {PicState(pic)}");
            var start = UnityEngine.Time.realtimeSinceStartup;
            while (UnityEngine.Time.realtimeSinceStartup - start < 4f && !Aborted(epoch)) yield return null;
            Log($"SHIELD (b) held 4 s: hotkey pings during hold={_pingCount - pings0} (auto: no human presses expected; registration ok=true) ignoreStill={IgnoreBits(ig)}");
        }
        finally { Release("shield.ignore"); }
        yield return Frames(2);
        Log("SHIELD after release: " + PicState(pic) + " ignore=" + IgnoreBits(ig) + " photoSrc=" + IgnoreBitsBySource(ig));
    }

    private static string PicState(PlayerInputController p)
    {
        try
        {
            return $"pic[IgnoreKeyboard={p.IgnoreKeyboard} IsSelfPhoto={p.IsSelfPhoto} IsMaskRot={p.IsMaskRot} focusUI={p.isFocusUI_} " +
                   $"moveType={p.MoveInputType} ignoreRotStretch={p.IgnoreRotAndStretch} touchOverUI={p.InputTouchCheckOverUICount} inputDir={p.inputDir_}]";
        }
        catch (Exception ex) { return $"pic[err {ex.GetType().Name}]"; }
    }

    /// <summary>One char per EInputMask bit (Move first): 1 = ignored.</summary>
    private static string IgnoreBits(ZIgnoreMgr ig)
    {
        var sb = new StringBuilder();
        try { foreach (EInputMask b in Enum.GetValues(typeof(EInputMask))) sb.Append(ig.IsInputIgnore(b) ? '1' : '0'); }
        catch (Exception ex) { sb.Append($"err:{ex.GetType().Name}"); }
        return sb.ToString();
    }

    private static string IgnoreBitsBySource(ZIgnoreMgr ig)
    {
        var sb = new StringBuilder();
        try { foreach (EInputMask b in Enum.GetValues(typeof(EInputMask))) sb.Append(ig.IsInputIgnoreBySource(b, EIgnoreMaskSource.EPhoto) ? '1' : '0'); }
        catch (Exception ex) { sb.Append($"err:{ex.GetType().Name}"); }
        return sb.ToString();
    }

    /// <summary>Runs a Lua chunk body under pcall (non-yielding only) and returns "ok &lt;value&gt;" / "ERR &lt;msg&gt;".</summary>
    private string Lua(string body)
    {
        if (!_services.Lua.Ready) return "LUA NOT READY";
        _services.Lua.DoString(
            "rawset(_G,'__fcp_out',nil); local _ok, _r = pcall(function() " + body + " end); " +
            "rawset(_G,'__fcp_out', _ok and ('ok ' .. tostring(_r)) or ('ERR ' .. tostring(_r)))");
        return _services.Lua.ReadGlobalString("__fcp_out") ?? "null";
    }
}
