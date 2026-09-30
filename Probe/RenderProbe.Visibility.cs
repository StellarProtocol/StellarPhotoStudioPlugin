using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Panda.Hud;
using Panda.Utility.Quality;
using Panda.ZGame;
using Panda.ZGame.Timeline;
using Panda.ZUi;
using Stellar.Abstractions.Services;
using UnityEngine;

namespace Stellar.PhotoStudioProbe;

public sealed partial class RenderProbe
{
    private IEnumerator StepEnumerate()
    {
        foreach (var n in new[] { "Panda.Hud.HudMgr", "Panda.Hud.HudUtility", "Panda.ZUi.UILayerVisibilityRules", "Panda.ZUi.ZUiRoot",
                     "Panda.ZGame.CameraFrameCtrl", "Panda.ZGame.Timeline.CutsceneManager", "Panda.ZGame.CameraManager",
                     "Panda.ZInput.PlayerInputController" })
            LogMethods(n);
        Try("settings", () =>
        {
            Log($"QualityGradeSetting.CharLimit={QualityGradeSetting.CharLimit} IsCharVisible={QualityGradeSetting.IsCharVisible}");
            var hud = HudMgr.Instance;
            Log($"HudMgr IsEnabled={hud.IsEnabled} IsActive={hud.IsActive} hudDisabledFlag_={hud.hudDisabledFlag_} " +
                $"charName={hud.GetHudSettingsShow(EHudSettingEntityType.EChar, EHudSettingFuncType.EName)} " +
                $"playerName={hud.GetHudSettingsShow(EHudSettingEntityType.EPlayer, EHudSettingFuncType.EName)}");
            var cuts = UILayerVisibilityRules.CutSceneHideUILayers;
            Log($"UILayerVisibilityRules.CutSceneHideUILayers=[{string.Join(",", Enumerable.Range(0, cuts?.Length ?? 0).Select(i => cuts![i].ToString()))}]");
        });
        Try("overlay-root", LogOverlayRoots);
        yield break;
    }

    private void LogMethods(string typeName)
    {
        var t = StellarInterop.FindType(typeName);
        if (t == null) { Log($"methods {typeName}: TYPE NOT FOUND"); return; }
        var names = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Distinct().OrderBy(s => s);
        Log($"methods {typeName} ({t.Assembly.GetName().Name}): {string.Join(" ", names)}");
    }

    private void LogOverlayRoots()
    {
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas) continue;
            Log($"root Canvas '{c.gameObject.name}' mode={c.renderMode} sort={c.sortingOrder} layer={c.gameObject.layer} active={c.gameObject.activeInHierarchy}");
        }
        var sw = GameObject.Find("StellarWindowCanvas");
        Log($"GameObject.Find(StellarWindowCanvas) -> {(sw == null ? "null" : sw.name + " children=" + sw.transform.childCount)}");
    }

    private IEnumerator StepHudHide()
    {
        var root = ZUiRoot.Instance;
        Log($"ZUiRoot before: layerMain visible={root.GetLayerVisible((int)EUiLayer.UILayerMain)}");
        root.SetUIInvisible(true);
        Log($"ZUiRoot.SetUIInvisible(true): layerMain visible={root.GetLayerVisible((int)EUiLayer.UILayerMain)}");
        yield return Snap("06a_hud_SetUIInvisible_true");
        root.SetUIInvisible(false);
        Log("ZUiRoot.SetUIInvisible(false) restored");
        yield return Snap("06a2_hud_restored");
    }

    private IEnumerator StepNameplates()
    {
        var hud = HudMgr.Instance;
        hud.SetHudSwitch(false, EHudAvailableSource.ECamera);
        Log($"HudMgr.SetHudSwitch(false, ECamera): IsEnabled={hud.IsEnabled} hudDisabledFlag_={hud.hudDisabledFlag_}");
        yield return Snap("06b_nameplates_SetHudSwitch_false");
        hud.SetHudSwitch(true, EHudAvailableSource.ECamera);
        Log($"HudMgr.SetHudSwitch(true, ECamera): IsEnabled={hud.IsEnabled} hudDisabledFlag_={hud.hudDisabledFlag_}");
        yield return Snap("06b2_nameplates_restored");
    }

    // E.CameraSystemShowEntityType (lua/common/enum_define.lua): Chum=2 Team=3 Union=4 Community=5 Stranger=6 OtherPlayer=11 OtherPet=15
    private static readonly int[] OtherEntityTypes = { 2, 3, 4, 5, 6, 11, 15 };

    private IEnumerator StepOtherPlayers()
    {
        if (!Try("CameraFrameCtrl.Instance", () => Log($"CameraFrameCtrl.Instance={(CameraFrameCtrl.Instance == null ? "null" : "ok")} IsCameraState={CameraFrameCtrl.IsCameraState}")))
            yield break;
        var cf = CameraFrameCtrl.Instance;
        cf.SetEntityShow(11, false);
        Log("CameraFrameCtrl.SetEntityShow(OtherPlayer=11, false)");
        yield return Snap("06c_others_SetEntityShow11_false");
        foreach (var t in OtherEntityTypes) cf.SetEntityShow(t, false);
        Log($"CameraFrameCtrl.SetEntityShow([{string.Join(",", OtherEntityTypes)}], false)");
        yield return Snap("06c2_others_SetEntityShow_all_false");
        foreach (var t in OtherEntityTypes) cf.SetEntityShow(t, true);
        Log("CameraFrameCtrl.SetEntityShow(all, true) restored");
        yield return Snap("06c3_others_restored");
        // Mechanism proof when no other player is in view: FriendlyNPCS=7 and Oneself=1 through the same call.
        cf.SetEntityShow(7, false);
        Log("CameraFrameCtrl.SetEntityShow(FriendlyNPCS=7, false)");
        yield return Snap("06c4_npcs_SetEntityShow7_false");
        cf.SetEntityShow(7, true);
        cf.SetEntityShow(1, false);
        Log("CameraFrameCtrl.SetEntityShow(7,true); SetEntityShow(Oneself=1, false)");
        yield return Snap("06c5_self_SetEntityShow1_false");
        cf.SetEntityShow(1, true);
        Log("CameraFrameCtrl.SetEntityShow(1, true) restored");
        yield return Snap("06c6_self_restored");
    }

    private IEnumerator StepCutsceneHiding()
    {
        var cm = CutsceneManager.Instance;
        const int character = (int)ContentHidingFlags.Character;
        cm.SetHidingFlags(character, true, CameraManager.ECameraCullingType.Cutscene);
        Log($"CutsceneManager.SetHidingFlags(Character=0x80, true, Cutscene) hasHiding={cm.hasHiding(character, 0)}");
        yield return Snap("06d_others_SetHidingFlags_Character");
        cm.SetHidingFlags(character, false, CameraManager.ECameraCullingType.Cutscene);
        Log("CutsceneManager.SetHidingFlags(Character, false) restored");
        yield return Snap("06d2_others_hiding_restored");
    }

    private bool Try(string what, Action a)
    {
        try { a(); return true; }
        catch (Exception ex) { Log($"{what} FAILED {ex.GetType().Name}: {ex.Message}"); return false; }
    }
}
