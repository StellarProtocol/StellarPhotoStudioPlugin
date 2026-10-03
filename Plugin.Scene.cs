using System;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

// The scene stays when the free camera is off (spec 2026-10-02-photo-studio-scene-stays-design.md): the freeze and the
// posed people belong to the StudioScene, not to the free camera. This partial holds the scene's lifetime and the members
// the panel's Scene group and the off-camera SCENE pill bind to (the UI itself lives elsewhere).
public sealed partial class Plugin
{
    private StudioScene _scene = null!;
    private SceneSelection _selection = null!;
    private Action _onSceneChanged = null!;
    private Action<bool> _onSceneCombatChanged = null!;

    /// <summary>True while Photo Studio holds a scene freeze (with or without the free camera).</summary>
    internal bool SceneFrozen => _scene.Frozen;

    /// <summary>How many people are posed in the scene now.</summary>
    internal int ScenePosedCount => _scene.PosedCount;

    /// <summary>Anything set up (frozen, or anyone posed) — what Reset scene would end.</summary>
    internal bool SceneIsSet => _scene.IsSet;

    /// <summary>Spec § 7: the off-camera SCENE pill shows while the scene is set, the free camera is off and the player is
    /// in the world (hiding it from captures is the pill window's own job).</summary>
    internal bool ScenePillVisible => _scene.IsSet && !_freeCam.Active && InWorld();

    private void StartScene()
    {
        _scene = new StudioScene(_services.SceneFreeze, _services.Posing);
        _onSceneChanged = OnSceneChanged;
        _scene.Changed += _onSceneChanged;
        // The SCENE pill's ⚔ IN COMBAT badge must refresh with the free camera off too (the camera's own combat
        // subscription lives only while it is on). Plugin-lifetime subscription.
        _onSceneCombatChanged = _ => _freeCamHudWin?.MarkDirty();
        _services.CombatState.Changed += _onSceneCombatChanged;
    }

    /// <summary>After the free camera exists: the Person group's selection routing (camera on → orbit; off → freeze centre).</summary>
    private void StartSceneSelection() =>
        _selection = new SceneSelection(_freeCam, _scene, _services.Posing, _services.EntityTransforms);

    /// <summary>Photo Studio unloading ends the scene (unfreeze, every posed person reset, kept entry hides released).</summary>
    private void StopScene()
    {
        _services.CombatState.Changed -= _onSceneCombatChanged;
        _scene.Changed -= _onSceneChanged;
        _scene.Dispose();
    }

    /// <summary>The Scene group's Freeze/Unfreeze button (Space does the same inside the free camera). Off the camera the
    /// freeze centres on the selected person — where the leash centres when the camera comes back (spec § 8).</summary>
    internal void ToggleSceneFreeze()
    {
        if (_freeCam.Active) _freeCam.ToggleFreeze();
        else if (InWorld() || _scene.Frozen) _scene.ToggleFreeze(_selection.PositionOf(SelectedPerson()));
    }

    /// <summary>The Scene group's Reset scene button: unfreezes and returns every posed person to normal; the free camera
    /// (if on) stays.</summary>
    internal void ResetScene()
    {
        _posingCtl.ForgetPoses();
        _scene.Reset();
    }

    private void OnSceneChanged()
    {
        _panelWin?.MarkDirty();
        SyncSceneHud();
    }

    /// <summary>The top HUD shows the camera line while the free camera is on and the SCENE pill while it is off with a
    /// scene set (spec § 7); one window, visibility driven by the two events, never polled.</summary>
    private void SyncSceneHud()
    {
        if (_freeCamHudWin is null) return;
        _freeCamHudWin.SetVisible(_freeCam.Active || _scene.IsSet);
        _freeCamHudWin.MarkDirty();
    }

    private EntityId SelectedPerson() =>
        _posingCtl.Subject.IsNone ? _services.CombatSnapshot.LocalEntityId : _posingCtl.Subject;
}
