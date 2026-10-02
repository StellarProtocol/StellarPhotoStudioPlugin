using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio;

/// <summary>
/// Where the Person group's selection goes (scene-stays spec §§ 5, 8): with the free camera on, the selection is the
/// orbit subject; off it, a frozen scene's centre follows the selected person as the camera's would — so the leash centres
/// on them when the camera comes back. Split out of Plugin.Scene.cs so the off-camera rule is testable. Main thread.
/// </summary>
internal sealed class SceneSelection
{
    private readonly FreeCamSession _camera;
    private readonly StudioScene _scene;
    private readonly IPosing _posing;
    private readonly IEntityTransforms _transforms;

    public SceneSelection(FreeCamSession camera, StudioScene scene, IPosing posing, IEntityTransforms transforms)
    {
        _camera = camera;
        _scene = scene;
        _posing = posing;
        _transforms = transforms;
    }

    /// <summary>The PosingHost's SetSubject: orbit them (camera on) or move a frozen scene's centre to them (camera off).
    /// A person the game no longer has leaves the centre where it is.</summary>
    public void Select(EntityId person)
    {
        if (_camera.Active) _camera.SetSubject(person);
        else if (_scene.Frozen && PositionOf(person) is { } at) _scene.MoveFreezeCentre(at);
    }

    /// <summary>Whether the game still shows the person (a posed copy / stand-in, or the entity itself).</summary>
    public bool IsSeen(EntityId person) => PositionOf(person) is not null;

    /// <summary>Where a person is seen: their posed copy / stand-in while there is one, else the entity; null when the game
    /// no longer has them.</summary>
    public Vector3? PositionOf(EntityId person)
    {
        if (_posing.TryGetVisiblePosition(person, out var posed)) return CameraMath.ToVec(posed);
        return _transforms.TryGetTransform(person, out var p, out _) ? CameraMath.ToVec(p) : null;
    }
}
