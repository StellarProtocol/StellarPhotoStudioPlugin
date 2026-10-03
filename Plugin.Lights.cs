using System;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Lights;
using Stellar.PhotoStudio.Presets;

namespace Stellar.PhotoStudio;

// Lights (spec devkit-freecam docs/superpowers/specs/2026-10-03-photo-studio-lights-design.md): the Lights tab's logic
// (LightsController) and its place in the scene. The tab UI, the markers and the L / Shift+L keys bind to the members below
// (and to _lights directly); they are built separately.
public sealed partial class Plugin
{
    private LightsController _lights = null!;
    private Action _onLightsChanged = null!;

    /// <summary>The Lights tab's controller (lamps, selection, placement, Light people, person key / rim, presets).</summary>
    internal LightsController LightsCtl => _lights;

    /// <summary>After the scene selection exists (the selected person anchors relative placement). Lights belong to the
    /// scene: they count toward <see cref="StudioScene.IsSet"/> and end on Reset scene / unload; the framework ends them
    /// itself on a zone change, cutscene or disconnect (<c>ILights.Released</c>).</summary>
    private void StartLights()
    {
        _lights = new LightsController(new LightsPorts(_services.Lights, SelectedPerson, LightAnchorOf, () => _freeCam.ShownPose));
        _onLightsChanged = OnLightsChanged;
        _lights.Changed += _onLightsChanged;
        _scene.TrackLights(() => _lights.SceneCount, _lights.Clear);
        _presetSession.Lights = new PresetLightsLink(_lights.Capture, p => _lights.Apply(p));
    }

    /// <summary>Before the scene ends on unload: the scene's own Dispose clears the lights through the tracked clear.</summary>
    private void StopLights() => _lights.Changed -= _onLightsChanged;

    /// <summary>L in the free camera: a lamp where the camera is.</summary>
    internal LightsResult DropLampAtCamera() => _lights.AddAtCamera();

    /// <summary>Shift+L in the free camera: the selected lamp moves to the camera.</summary>
    internal LightsResult MoveLampToCamera() => _lights.MoveSelectedToCamera();

    private void OnLightsChanged()
    {
        _scene.NotifyLightsChanged();
        _panelWin?.MarkDirty();
    }

    /// <summary>Where a person is seen (posed copy / stand-in, else the entity) and which way the entity faces.</summary>
    private LightAnchor? LightAnchorOf(EntityId person)
    {
        if (person.IsNone || _selection.PositionOf(person) is not { } at) return null;
        var yaw = _services.EntityTransforms.TryGetTransform(person, out _, out var y) ? y : 0f;
        return new LightAnchor(at, yaw);
    }
}
