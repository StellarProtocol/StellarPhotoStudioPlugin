using System;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

// The scene stays when the free camera is off (spec 2026-10-02-photo-studio-scene-stays-design.md): the freeze and the
// posed people belong to the StudioScene, not to the free camera. This partial holds the scene's lifetime and the members
// the panel's Scene group and the off-camera SCENE pill bind to (the UI itself lives elsewhere).
public sealed partial class Plugin
{
    private StudioScene _scene = null!;
    private Action _onSceneChanged = null!;

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
    }

    /// <summary>Photo Studio unloading ends the scene (unfreeze, every posed person reset).</summary>
    private void StopScene()
    {
        _scene.Changed -= _onSceneChanged;
        _scene.Dispose();
    }

    /// <summary>The Scene group's Freeze/Unfreeze button (Space does the same inside the free camera). Off the camera the
    /// freeze centres on the selected person — where the leash centres when the camera comes back (spec § 8).</summary>
    internal void ToggleSceneFreeze()
    {
        if (_freeCam.Active) _freeCam.ToggleFreeze();
        else if (InWorld() || _scene.Frozen) _scene.ToggleFreeze(PersonPosition(SelectedPerson()));
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
        _freeCamHudWin?.MarkDirty();
    }

    /// <summary>From the Person group's selection (PosingHost.SetSubject): with the free camera on, the selection is the
    /// orbit subject; off it, a frozen scene's centre follows the selection as the camera's would (spec § 8).</summary>
    private void SelectInScene(EntityId person)
    {
        if (_freeCam.Active) _freeCam.SetSubject(person);
        else if (_scene.Frozen && PersonPosition(person) is { } at) _scene.MoveFreezeCentre(at);
    }

    private EntityId SelectedPerson() =>
        _posingCtl.Subject.IsNone ? _services.CombatSnapshot.LocalEntityId : _posingCtl.Subject;

    /// <summary>Where a person is seen: their posed copy / stand-in while there is one, else the entity; null when the game
    /// no longer has them.</summary>
    private Vector3? PersonPosition(EntityId person)
    {
        if (_services.Posing.TryGetVisiblePosition(person, out var posed)) return CameraMath.ToVec(posed);
        return _services.EntityTransforms.TryGetTransform(person, out var p, out _) ? CameraMath.ToVec(p) : null;
    }
}
