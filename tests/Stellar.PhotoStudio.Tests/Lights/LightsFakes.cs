using System;
using System.Collections.Generic;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Lights;

namespace Stellar.PhotoStudio.Tests.Lights;

/// <summary>The framework's ILights as the plugin sees it: lamps by id, the level, people lights; Released on demand.</summary>
internal sealed class FakeLights : ILights
{
    public readonly Dictionary<int, LampSettings> Lamps = new();
    public readonly Dictionary<EntityId, PersonLight> People = new();
    public readonly List<string> Calls = new();
    public bool IsAvailable { get; set; } = true;
    public float PeopleLevel { get; set; }
    public event Action? Released;
    private int _next;

    public LampId AddLamp(LampSettings settings)
    {
        Calls.Add("add");
        if (!IsAvailable || Lamps.Count >= LightLimits.MaxLampsPerPlugin) return LampId.None;
        Lamps[++_next] = settings;
        return new LampId(_next);
    }

    public bool UpdateLamp(LampId lamp, LampSettings settings)
    {
        Calls.Add("update");
        if (!IsAvailable || !Lamps.ContainsKey(lamp.Value)) return false;
        Lamps[lamp.Value] = settings;
        return true;
    }

    public void RemoveLamp(LampId lamp)
    {
        Calls.Add("remove");
        Lamps.Remove(lamp.Value);
    }

    public bool SetPersonLight(EntityId person, PersonLight light)
    {
        Calls.Add("person");
        if (!IsAvailable) return false;
        if (light.IsNone) People.Remove(person);
        else People[person] = light;
        return true;
    }

    public void ResetAll()
    {
        Lamps.Clear();
        People.Clear();
    }

    /// <summary>The framework ended the scene (zone change …): everything already gone.</summary>
    public void EndScene()
    {
        Lamps.Clear();
        People.Clear();
        Released?.Invoke();
    }

    public Vector3 Pos(int id) => new(Lamps[id].Position.X, Lamps[id].Position.Y, Lamps[id].Position.Z);
}

/// <summary>A LightsController over the fake: people 1 (you, at the origin facing +Z) and 2 (at (10,0,0) facing +X).</summary>
internal sealed class LightsRig
{
    public readonly FakeLights Lights = new();
    public readonly Dictionary<long, LightAnchor> Anchors = new()
    {
        [1] = new LightAnchor(Vector3.Zero, 0f),
        [2] = new LightAnchor(new Vector3(10f, 0f, 0f), 90f),
    };
    public EntityId Selected = new(1);
    public (Vector3 Position, float Yaw)? Camera = (new Vector3(1f, 1.6f, -3f), 0f);
    public readonly LightsController Ctl;
    public int Changes;

    public LightsRig()
    {
        Ctl = new LightsController(new LightsPorts(Lights, () => Selected,
            id => Anchors.TryGetValue(id.Value, out var a) ? a : null, () => Camera));
        Ctl.Changed += () => Changes++;
    }
}
