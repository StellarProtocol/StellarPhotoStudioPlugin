using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.Posing;

/// <summary>What the Person group needs from the plugin around it (plain callbacks keep the controller testable).</summary>
/// <param name="Subject">The free camera's orbit subject now.</param>
/// <param name="SetSubject">Makes a person the orbit subject — selecting a person IS orbiting them (spec § 3).</param>
/// <param name="Refused">A pose could not be played (Refused: the game showed its own message; Unavailable: ours).</param>
internal sealed record PosingHost(Func<EntityId> Subject, Action<EntityId> SetSubject, Action<PoseResult> Refused);

/// <summary>
/// The Person group's logic (spec 2026-10-02 §§ 3–4). One selection: the free camera's orbit subject. Per person, the
/// panel's state (<see cref="PersonUiState"/>) and the framework's <see cref="IPoseTarget"/>, fetched on the first control
/// (the framework then makes the copy / model) and dropped once released. The framework resets every touched person when
/// the free camera ends; <see cref="Clear"/> then forgets the panel state. Main thread.
/// </summary>
internal sealed class PosingController
{
    public const float PeopleRadius = 40f;
    public const float AimStep = 0.25f;
    public const float MaxYaw = 180f;

    private readonly IPosing _posing;
    private readonly PosingHost _host;
    private readonly Dictionary<long, PersonUiState> _states = new();
    private readonly Dictionary<long, IPoseTarget> _targets = new();
    private readonly Dictionary<long, PersonInfo> _known = new();

    public PosingController(IPosing posing, PosingHost host)
    {
        _posing = posing;
        _host = host;
    }

    public EntityId Subject { get; private set; }
    public PersonInfo? Person => _known.TryGetValue(Subject.Value, out var p) ? p : null;
    public PersonUiState State => StateOf(Subject.Value);
    public PoseResult LastResult { get; private set; } = PoseResult.Applied;
    public bool Available => _posing.IsAvailable && !Subject.IsNone;
    public bool IsCopy => Person?.Kind == PersonKind.Player;
    public PoseTargetState TargetState => _targets.TryGetValue(Subject.Value, out var t) ? t.State : PoseTargetState.Idle;
    public bool Loading => TargetState == PoseTargetState.Loading;
    public bool Failed => TargetState == PoseTargetState.Failed;
    /// <summary>The game's photo-member limit is reached for this kind of person (the row says so; reset someone).</summary>
    public bool Full => TargetState == PoseTargetState.Full;
    public bool ShowClothHint => State.Action is not null && !State.Playing;
    public IReadOnlyList<ExpressionInfo> Expressions => _posing.Expressions;

    public string ExpressionName
    {
        get
        {
            var i = State.ExpressionIndex;
            var list = Expressions;
            return i >= 0 && i < list.Count ? list[i].Name : "";
        }
    }

    /// <summary>Follows the orbit subject (called when the free camera's state changes — event-driven). A person not seen
    /// yet costs one people read.</summary>
    public void SyncSubject()
    {
        var s = _host.Subject();
        if (s == Subject) return;
        Subject = s;
        if (!s.IsNone && !_known.ContainsKey(s.Value)) Remember(_posing.NearbyPeople(PeopleRadius));
    }

    public void Cycle(int direction)
    {
        var people = _posing.NearbyPeople(PeopleRadius);
        Remember(people);
        var next = PersonCycle.Next(people, Subject, direction);
        if (!next.IsNone && next != Subject) _host.SetSubject(next);
    }

    public void Play(EmoteInfo action)
    {
        if (Target() is not { } t) return;
        var s = State;
        s.Action = action;
        s.Playing = true;
        s.Moment = 0f;
        Report(t.PlayAction(action.Id));
    }

    /// <summary>❚❚ holds the pose where it is now (one game read); ▶ lets it play again.</summary>
    public void TogglePlay()
    {
        var s = State;
        if (s.Action is null || Target() is not { } t) return;
        if (!s.Playing)
        {
            t.Moment = -1f;
            s.Playing = true;
            return;
        }
        var live = t.Moment;
        if (live >= 0f) s.Moment = live;
        t.Moment = s.Moment;
        s.Playing = false;
    }

    public void Restart()
    {
        if (State.Action is { } action) Play(action);
    }

    public void SetMoment(float value)
    {
        var s = State;
        if (s.Action is null || Target() is not { } t) return;
        s.Moment = Math.Clamp(value, 0f, 1f);
        s.Playing = false;
        t.Moment = s.Moment;
    }

    public void CycleExpression(int direction)
    {
        var list = Expressions;
        if (list.Count == 0 || Target() is not { } t) return;
        var s = State;
        var n = list.Count + 1;   // "none" (−1) sits before the first expression
        s.ExpressionIndex = ((s.ExpressionIndex + 1 + Math.Sign(direction)) % n + n) % n - 1;
        t.SetExpression(s.ExpressionIndex < 0 ? 0 : list[s.ExpressionIndex].Id, s.Hold);
    }

    public void ToggleHold()
    {
        var s = State;
        s.Hold = !s.Hold;
        var list = Expressions;
        if (s.ExpressionIndex >= 0 && s.ExpressionIndex < list.Count && Target() is { } t)
            t.SetExpression(list[s.ExpressionIndex].Id, s.Hold);
    }

    public void SetLook(LookPart part, LookMode mode)
    {
        if (Target() is not { } t) return;
        var s = State;
        s.SetMode(part, mode);
        t.SetLook(part, mode, s.Locked(part));
    }

    public void ToggleLock(LookPart part)
    {
        if (Target() is not { } t) return;
        var s = State;
        s.SetLocked(part, !s.Locked(part));
        t.SetLook(part, s.Mode(part), s.Locked(part));
    }

    /// <summary>The arrow pad: one step of <see cref="AimStep"/> per press (clamped to ±1); the centre button re-centres.</summary>
    public void Aim(LookPart part, int dx, int dy)
    {
        if (Target() is not { } t) return;
        var s = State;
        var centre = dx == 0 && dy == 0;
        var x = centre ? 0f : Math.Clamp(s.AimX(part) + dx * AimStep, -1f, 1f);
        var y = centre ? 0f : Math.Clamp(s.AimY(part) + dy * AimStep, -1f, 1f);
        s.SetAim(part, x, y);
        t.Aim(part, x, y);
    }

    public void SetYaw(float degrees)
    {
        if (Target() is not { } t) return;
        var s = State;
        s.Yaw = Math.Clamp(degrees, -MaxYaw, MaxYaw);
        t.Yaw = s.Yaw;
    }

    public void ResetPerson()
    {
        var key = Subject.Value;
        if (_targets.TryGetValue(key, out var t)) t.Reset();
        _states.Remove(key);
    }

    /// <summary>From <see cref="IPosing.Changed"/>: people the framework released (left, or the free camera ended) are
    /// forgotten; the next control selects them again.</summary>
    public void OnPosingChanged()
    {
        var gone = new List<long>();
        foreach (var kv in _targets)
            if (kv.Value.State == PoseTargetState.Released) gone.Add(kv.Key);
        foreach (var key in gone)
        {
            _targets.Remove(key);
            _states.Remove(key);
        }
    }

    public void Clear()
    {
        _targets.Clear();
        _states.Clear();
        _known.Clear();
        Subject = EntityId.None;
        LastResult = PoseResult.Applied;
    }

    private IPoseTarget? Target()
    {
        if (!Available) return null;
        var key = Subject.Value;
        if (_targets.TryGetValue(key, out var known) && known.State != PoseTargetState.Released) return known;
        var selected = _posing.Select(Subject);
        if (selected is null) { _targets.Remove(key); return null; }
        _targets[key] = selected;
        return selected;
    }

    private PersonUiState StateOf(long key)
    {
        if (!_states.TryGetValue(key, out var s)) _states[key] = s = new PersonUiState();
        return s;
    }

    private void Remember(IReadOnlyList<PersonInfo> people)
    {
        foreach (var p in people) _known[p.Id.Value] = p;
    }

    private void Report(PoseResult result)
    {
        LastResult = result;
        if (result is PoseResult.Refused or PoseResult.Unavailable) _host.Refused(result);
    }
}
