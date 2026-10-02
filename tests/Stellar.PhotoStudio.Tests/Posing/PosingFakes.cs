using System;
using System.Collections.Generic;
using System.Globalization;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Posing;

namespace Stellar.PhotoStudio.Tests.Posing;

internal sealed class FakePoseTarget : IPoseTarget
{
    public readonly List<string> Calls = new();
    public PoseTargetState State { get; set; } = PoseTargetState.Idle;
    public PoseResult PlayResult = PoseResult.Applied;
    public float LiveMoment = 0.37f;
    private float _moment = -1f, _yaw;
    private static string N(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public PoseResult PlayAction(int actionId)
    {
        Calls.Add($"play {actionId}");
        _moment = -1f;
        State = PlayResult == PoseResult.Full ? PoseTargetState.Full : PoseTargetState.Ready;
        return PlayResult;
    }

    public float Moment
    {
        get => _moment >= 0f ? _moment : LiveMoment;
        set { _moment = value; Calls.Add($"moment {N(value)}"); }
    }

    public void SetExpression(int expressionId, bool hold) => Calls.Add($"face {expressionId} hold={hold}");
    public void SetLook(LookPart part, LookMode mode, bool locked) => Calls.Add($"look {part} {mode} lock={locked}");
    public void Aim(LookPart part, float x, float y) => Calls.Add($"aim {part} {N(x)},{N(y)}");

    public float Yaw
    {
        get => _yaw;
        set { _yaw = value; Calls.Add($"yaw {value.ToString("0", CultureInfo.InvariantCulture)}"); }
    }

    public void Reset() { Calls.Add("reset"); State = PoseTargetState.Idle; }
}

internal sealed class FakePosing : IPosing
{
    public bool IsAvailable { get; set; } = true;
    public List<PersonInfo> People = new()
    {
        new(new EntityId(1), "Revette", PersonKind.Self, 0f),
        new(new EntityId(2), "Celia", PersonKind.Player, 3f),
        new(new EntityId(3), "Mira", PersonKind.Npc, 6f),
        // A camera-picked subject beyond the normal 40 m scan (the camera can reach ~60 m out): only the wider
        // PosingController.SubjectSearchRadius fallback query finds them.
        new(new EntityId(4), "Dax", PersonKind.Player, 60f),
    };
    public List<ExpressionInfo> Faces = new() { new(1003, "Angry", 303, 403), new(1015, "Startled", 315, 415) };
    public readonly Dictionary<long, FakePoseTarget> Targets = new();
    public readonly Dictionary<long, Position3D> Visible = new();
    public int Selects, PeopleReads;
    public event Action? Changed;

    public bool TryGetVisiblePosition(EntityId person, out Position3D position) => Visible.TryGetValue(person.Value, out position);

    /// <summary>What each person is doing right now (the game's running action); absent = idle.</summary>
    public readonly Dictionary<long, (int Id, float Moment)> Running = new();
    public int ActionReads;

    public bool TryGetCurrentAction(EntityId person, out int actionId, out float moment)
    {
        ActionReads++;
        if (IsAvailable && Running.TryGetValue(person.Value, out var r)) { actionId = r.Id; moment = r.Moment; return true; }
        actionId = 0;
        moment = -1f;
        return false;
    }

    /// <summary>Distance-filtered like the real game read, so a test can tell a 40 m scan from a wider fallback one.</summary>
    public IReadOnlyList<PersonInfo> NearbyPeople(float radius)
    {
        PeopleReads++;
        return People.FindAll(p => p.Distance <= radius);
    }
    public IReadOnlyList<ExpressionInfo> Expressions => Faces;

    public IPoseTarget? Select(EntityId person)
    {
        Selects++;
        if (!IsAvailable || person.IsNone) return null;
        if (!Targets.TryGetValue(person.Value, out var t) || t.State == PoseTargetState.Released)
            Targets[person.Value] = t = new FakePoseTarget();
        return t;
    }

    public int ResetAllCalls;

    /// <summary>The framework's Changed after a target's state moved on its own (a model loaded or failed).</summary>
    public void RaiseChanged() => Changed?.Invoke();

    public void ResetAll()
    {
        ResetAllCalls++;
        foreach (var t in Targets.Values) t.State = PoseTargetState.Released;
        Changed?.Invoke();
    }
}

internal sealed class PosingRig
{
    public readonly FakePosing Posing = new();
    public readonly List<PoseResult> Refusals = new();
    public readonly PosingController Ctl;
    public EntityId Subject = new(1);
    public EntityId LocalId = new(1);
    public static readonly EmoteInfo Dance = new(9020, "Dance I", "", false);
    public const string CurrentPose = "Current pose";

    public PosingRig()
    {
        Ctl = new PosingController(Posing, new PosingHost(() => Subject, id => { Subject = id; Ctl!.SyncSubject(); }, Refusals.Add, () => LocalId,
            id => id == Dance.Id ? new DescribedAction(Dance, true) : new DescribedAction(new EmoteInfo(id, CurrentPose, "", false), false)));
        Ctl.SyncSubject();
    }

    public void Select(long id)
    {
        Subject = new EntityId(id);
        Ctl.SyncSubject();
    }

    public FakePoseTarget Target(long id) => Posing.Targets[id];
}
