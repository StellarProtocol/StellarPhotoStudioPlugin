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
        _lights = new LightsController(new LightsPorts(_services.Lights, SelectedPerson, LightAnchorOf, () => _freeCam.ShownPose),
            _settings.PeopleLevel);
        _onLightsChanged = OnLightsChanged;
        _lights.Changed += _onLightsChanged;
        _scene.TrackLights(() => _lights.SceneCount, _lights.Clear);
        _presetSession.Lights = new PresetLightsLink(_lights.Capture, _lights.Apply);
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
        if (_lights.PeopleLevel == _settings.PeopleLevel) return;
        _settings.SetPeopleLevel(_lights.PeopleLevel, save: false);   // the slider fires every frame: save once it settles
        _peopleLevelSaveIn = PeopleLevelSaveDelay;
    }

    private const float PeopleLevelSaveDelay = 0.5f;
    private float _peopleLevelSaveIn = -1f;

    /// <summary>From OnUpdate: saves Light people once its slider has settled.</summary>
    private void TickLightsSave(float dt)
    {
        if (_peopleLevelSaveIn < 0f) return;
        _peopleLevelSaveIn -= dt;
        if (_peopleLevelSaveIn <= 0f) FlushPeopleLevel();
    }

    private void FlushPeopleLevel()
    {
        if (_peopleLevelSaveIn < 0f) return;
        _peopleLevelSaveIn = -1f;
        _settings.SetPeopleLevel(_settings.PeopleLevel, save: true);
    }

    /// <summary>Lit people the game no longer shows are forgotten (IPosing.Changed and the panel's poll).</summary>
    private void PruneLitPeople() => _lights.PrunePeople(_isSeen);

    /// <summary>Where a person is seen (posed copy / stand-in, else the entity) and which way that model faces: the entity's
    /// facing plus the Face turn of a posed copy (lights review minor — lamps placed around a turned copy follow it).</summary>
    private LightAnchor? LightAnchorOf(EntityId person)
    {
        if (person.IsNone || _selection.PositionOf(person) is not { } at) return null;
        var yaw = _services.EntityTransforms.TryGetTransform(person, out _, out var y) ? y : 0f;
        return new LightAnchor(at, LightsMath.Wrap(yaw + _posingCtl.PosedYaw(person)));
    }
}
