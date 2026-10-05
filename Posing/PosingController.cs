using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.Posing;

/// <summary>What the Person group needs from the plugin around it (plain callbacks keep the controller testable).</summary>
/// <param name="Subject">The free camera's orbit subject now (read by <see cref="PosingController.SyncSubject"/>, which runs
/// only while the free camera is on).</param>
/// <param name="SetSubject">Makes a person the orbit subject while the free camera is on — selecting a person IS orbiting
/// them (spec § 3); does nothing while it is off (the selection is then the controller's own).</param>
/// <param name="Refused">A pose could not be played (Refused: the game showed its own message; Unavailable: ours).</param>
/// <param name="LocalEntityId">The local player's entity id — used only to tell whether the subject is you
/// (<see cref="PosingController.SubjectIsSelf"/>), independent of whether a <see cref="PersonInfo"/> was ever
/// resolved for them.</param>
/// <param name="DescribeAction">The emote for an action id a person is already doing: the unlocked emote's entry when it
/// is one, else a generic "current pose" entry carrying that id (<see cref="DescribedAction.Unlocked"/> false).</param>
internal sealed record PosingHost(Func<EntityId> Subject, Action<EntityId> SetSubject, Action<PoseResult> Refused, Func<EntityId> LocalEntityId,
    Func<int, DescribedAction> DescribeAction);

/// <summary>An action a person is already doing, named for the panel; <paramref name="Unlocked"/> = one of the player's
/// unlocked emotes (only those can be replayed with ↺).</summary>
internal readonly record struct DescribedAction(EmoteInfo Emote, bool Unlocked);

/// <summary>
/// The Person group's logic (spec 2026-10-02 §§ 3–4; scene-stays spec § 5). The controller keeps its own selected person,
/// so posing works with the free camera off (‹ › select without a camera); while the free camera is on, the selection and
/// the orbit subject are one (selecting orbits that person, picking in the camera selects). Per person, the panel's state
/// (<see cref="PersonUiState"/>) and the framework's <see cref="IPoseTarget"/>, fetched on the first control (the framework
/// then makes the copy / model) and dropped once released. Poses belong to the scene: leaving the free camera keeps them;
/// the Reset scene button (<see cref="ForgetPoses"/> + <see cref="IPosing.ResetAll"/>) and the framework's own scene-end
/// reasons (targets released → <see cref="OnPosingChanged"/>) end them. Main thread.
/// </summary>
internal sealed class PosingController
{
    public const float PeopleRadius = 40f;
    /// <summary>Fallback search radius when the orbit subject (picked by the camera, which can reach ~60 m) is not
    /// among the people <see cref="PeopleRadius"/> already found — queried once per subject, never every frame.</summary>
    public const float SubjectSearchRadius = 100f;
    /// <summary>Arrow-pad steps of the aim range (−1…+1): Fine, Normal, Coarse (player request 2026-10-05 — 0.25 alone was
    /// too coarse). Normal is the default.</summary>
    public static readonly float[] AimSteps = { 0.05f, 0.1f, 0.25f };
    public const float MaxYaw = 180f;

    private readonly IPosing _posing;
    private readonly PosingHost _host;
    private readonly Dictionary<long, PersonUiState> _states = new();
    private readonly Dictionary<long, IPoseTarget> _targets = new();
    private readonly Dictionary<long, PersonInfo> _known = new();
    private readonly List<long> _releasedScratch = new();
    private int _announcedPosed;

    public PosingController(IPosing posing, PosingHost host)
    {
        _posing = posing;
        _host = host;
    }

    /// <summary>The selected person (the free camera's orbit subject while it is on).</summary>
    public EntityId Subject { get; private set; }

    /// <summary>How many people are really posed now: touched through a control (any control — a facing or look-only
    /// touch counts) and live: Idle / Loading / Ready. A target the framework could not make (Failed, Full — including
    /// a Loading model that then failed) or released is not posed (review minor: the SCENE pill / status said "1 posed"
    /// for a person nothing was done to).</summary>
    public int PosedCount
    {
        get
        {
            var n = 0;
            foreach (var t in _targets.Values)
                if (IsPosed(t.State)) n++;
            return n;
        }
    }

    /// <summary>Raised when <see cref="PosedCount"/> changed (someone posed, failed, reset, released or forgotten).</summary>
    public event Action? PosedChanged;
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
    /// <summary>↺ can replay the shown action: a user pick, or a detected action that is one of the unlocked emotes (fix
    /// round 1 (7) — a detected action outside the list would only be refused by the game).</summary>
    public bool CanRestart => State.Action is not null && (!State.Detected || State.ActionUnlocked);
    public IReadOnlyList<ExpressionInfo> Expressions => _posing.Expressions;
    /// <summary>Whether the orbit subject is the local player — true even when no <see cref="PersonInfo"/> was ever
    /// resolved for them (a person beyond even <see cref="SubjectSearchRadius"/>), so the UI never falls back to
    /// "you" just because <see cref="Person"/> is null.</summary>
    public bool SubjectIsSelf => !Subject.IsNone && Subject == _host.LocalEntityId();

    public string ExpressionName
    {
        get
        {
            var i = State.ExpressionIndex;
            if (i < 0) return "";
            var list = Expressions;
            return i < list.Count ? list[i].Name : "";
        }
    }

    /// <summary>Follows the orbit subject (called when the free camera's state changes — event-driven). A person not seen
    /// yet costs one people read at <see cref="PeopleRadius"/>; one picked beyond that (the camera can reach ~60 m) costs
    /// one more at <see cref="SubjectSearchRadius"/>, so a far pick still resolves a name/kind instead of showing "you"
    /// with no copy note.</summary>
    public void SyncSubject() => Adopt(_host.Subject());

    /// <summary>Selects <paramref name="person"/> — with the free camera on, that also makes them the orbit subject.</summary>
    public void Select(EntityId person)
    {
        if (person.IsNone || person == Subject) return;
        Adopt(person);
        _host.SetSubject(person);
    }

    /// <summary>With nobody selected (start, or after <see cref="Clear"/>), selects yourself. Cheap: a no-op once someone
    /// is selected.</summary>
    public void EnsureSubject()
    {
        if (Subject.IsNone) Adopt(_host.LocalEntityId());
    }

    /// <summary>Off the free camera (from the panel's ~10 Hz tick): a selected person who left — no live pose target, and
    /// <paramref name="seen"/> (a visible copy or an entity transform) says the game no longer has them — falls back to
    /// yourself. Never moves the freeze centre (the host's SetSubject is not called). True when the selection changed.</summary>
    public bool FallBackIfGone(Func<EntityId, bool> seen)
    {
        if (Subject.IsNone || SubjectIsSelf || HasLiveTarget(Subject) || seen(Subject)) return false;
        Adopt(_host.LocalEntityId());
        return true;
    }

    /// <summary>Degrees <paramref name="person"/>'s shown model is turned from the facing they had when first posed (the
    /// Face slider); 0 when they are not posed. Lamps placed around a posed person follow it (lights review minor).</summary>
    public float PosedYaw(EntityId person) =>
        _targets.TryGetValue(person.Value, out var t) && IsPosed(t.State) ? t.Yaw : 0f;

    private bool HasLiveTarget(EntityId person) =>
        _targets.TryGetValue(person.Value, out var t) && t.State != PoseTargetState.Released;

    private void Adopt(EntityId s)
    {
        if (s == Subject) return;
        Subject = s;
        if (!s.IsNone && !_known.ContainsKey(s.Value))
        {
            Remember(_posing.NearbyPeople(PeopleRadius));
            if (!_known.ContainsKey(s.Value)) Remember(_posing.NearbyPeople(SubjectSearchRadius));
        }
        PollCurrentAction();
    }

    /// <summary>Owner bug 2026-10-02: a person already doing an emote shows it, playing, with the Moment following it —
    /// never "Pick a pose" at 0 %. Called on a subject change and on each panel poll (~10 Hz; one cheap framework read).
    /// Stops once the user sets the pose (<see cref="PersonUiState.UserPosed"/>) until the person is reset. True when
    /// what the panel shows changed.</summary>
    public bool PollCurrentAction()
    {
        // NPCs are never auto-detected (fix round 1 (1)): their posed stand-in cannot carry the live NPC's action.
        if (!Available || Person?.Kind == PersonKind.Npc) return false;
        var s = State;
        if (s.UserPosed) return false;
        if (!_posing.TryGetCurrentAction(Subject, out var id, out var moment))
        {
            if (!s.Detected) return false;
            ForgetDetected(s);
            return true;
        }
        var changed = !s.Detected || s.Action?.Id != id || !s.Playing || s.Moment != moment;
        if (s.Action?.Id != id) Describe(s, id);
        s.Playing = true;
        s.Moment = moment;
        s.Detected = true;
        return changed;
    }

    public void Cycle(int direction)
    {
        var people = _posing.NearbyPeople(PeopleRadius);
        Remember(people);
        Select(PersonCycle.Next(people, Subject, direction));
    }

    public void Play(EmoteInfo action)
    {
        if (Target() is not { } t) return;
        var s = State;
        s.Action = action;
        s.ActionUnlocked = true;
        s.Detected = false;
        s.Playing = true;
        s.Moment = 0f;
        TakeOver(s);
        Report(t.PlayAction(action.Id));
        AnnouncePosed();   // a Full / Failed answer un-counts the person Target() just counted
    }

    /// <summary>❚❚ holds the pose where it is now (one game read); ▶ lets it play again. A detected action (the person's
    /// own) is held where it is — the framework adopts it, it is never played again from the start.</summary>
    public void TogglePlay()
    {
        var s = State;
        if (s.Action is null || Target() is not { } t) return;
        if (!s.Playing)
        {
            t.Moment = -1f;
            s.Playing = true;
            TakeOver(s);
            return;
        }
        if (s.Detected && !s.UserPosed)
        {
            // Fix round 1 (6): the poll may be up to 0.1 s old. Nothing running any more → show "Pick a pose", hold
            // nothing; a different action → name it, then hold that one.
            if (!_posing.TryGetCurrentAction(Subject, out var id, out var now))
            {
                ForgetDetected(s);
                return;
            }
            if (id != s.Action.Id) Describe(s, id);
            s.Moment = now;
        }
        else
        {
            var live = t.Moment;
            if (live >= 0f) s.Moment = live;
        }
        t.Moment = s.Moment;
        s.Playing = false;
        TakeOver(s);
    }

    public void Restart()
    {
        if (CanRestart && State.Action is { } action) Play(action);
    }

    public void SetMoment(float value)
    {
        var s = State;
        if (s.Action is null || Target() is not { } t) return;
        s.Moment = Math.Clamp(value, 0f, 1f);
        s.Playing = false;
        TakeOver(s);
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

    /// <summary>The arrow pad: one <paramref name="step"/> per press (clamped to ±1); the centre button re-centres.</summary>
    public void Aim(LookPart part, int dx, int dy, float step)
    {
        var s = State;
        var centre = dx == 0 && dy == 0;
        SetAim(part, centre ? 0f : s.AimX(part) + dx * step, centre ? 0f : s.AimY(part) + dy * step);
    }

    /// <summary>Points the head or eyes at (x, y) in the aim range, clamped to ±1 (the drag pad and the arrow pad).</summary>
    public void SetAim(LookPart part, float x, float y)
    {
        if (Target() is not { } t) return;
        x = Math.Clamp(x, -1f, 1f);
        y = Math.Clamp(y, -1f, 1f);
        State.SetAim(part, x, y);
        t.Aim(part, x, y);
    }

    public void SetYaw(float degrees)
    {
        if (Target() is not { } t) return;
        var s = State;
        s.Yaw = Math.Clamp(degrees, -MaxYaw, MaxYaw);
        t.Yaw = s.Yaw;
    }

    /// <summary>Returns the subject to normal and forgets them — including from a <see cref="PoseTargetState.Failed"/> or
    /// stuck <see cref="PoseTargetState.Loading"/> state, the only way out of either (the framework holds Failed until
    /// <c>Reset()</c>). The next control re-selects fresh, so a Loading target's pending callback (already cancelled by
    /// the framework's own generation check) never lands on a reused object.</summary>
    public void ResetPerson()
    {
        var key = Subject.Value;
        if (_targets.TryGetValue(key, out var t)) t.Reset();
        _targets.Remove(key);
        _states.Remove(key);
        AnnouncePosed();
    }

    /// <summary>From <see cref="IPosing.Changed"/>: people the framework released (left, the scene ended, or the scene was
    /// reset) are forgotten; the next control selects them again. A model that finished loading or failed changes the
    /// count too.</summary>
    public void OnPosingChanged()
    {
        _releasedScratch.Clear();
        foreach (var kv in _targets)
            if (kv.Value.State == PoseTargetState.Released) _releasedScratch.Add(kv.Key);
        foreach (var key in _releasedScratch)
        {
            _targets.Remove(key);
            _states.Remove(key);
        }
        AnnouncePosed();
    }

    /// <summary>The Reset scene button (before <see cref="IPosing.ResetAll"/>): forgets every person's pose and panel state;
    /// the selection stays.</summary>
    public void ForgetPoses()
    {
        _targets.Clear();
        _states.Clear();
        LastResult = PoseResult.Applied;
        AnnouncePosed();
    }

    /// <summary>Forgets everything, the selection too (the world was left: entity ids no longer mean anyone).</summary>
    public void Clear()
    {
        ForgetPoses();
        _known.Clear();
        Subject = EntityId.None;
    }

    private IPoseTarget? Target()
    {
        if (!Available) return null;
        var key = Subject.Value;
        if (_targets.TryGetValue(key, out var known) && known.State != PoseTargetState.Released) return known;
        var selected = _posing.Select(Subject);
        if (selected is null) { _targets.Remove(key); return null; }
        _targets[key] = selected;
        AnnouncePosed();
        return selected;
    }

    private static bool IsPosed(PoseTargetState state) =>
        state is PoseTargetState.Idle or PoseTargetState.Loading or PoseTargetState.Ready;

    private void AnnouncePosed()
    {
        var n = PosedCount;
        if (n == _announcedPosed) return;
        _announcedPosed = n;
        PosedChanged?.Invoke();
    }

    private void Describe(PersonUiState s, int id)
    {
        var d = _host.DescribeAction(id);
        s.Action = d.Emote;
        s.ActionUnlocked = d.Unlocked;
    }

    private static void ForgetDetected(PersonUiState s)
    {
        s.Action = null;
        s.Playing = false;
        s.Moment = 0f;
        s.Detected = false;
        s.ActionUnlocked = true;
    }

    // The user set the pose: our own state wins from now on (no more auto-detect for this person until Reset).
    private static void TakeOver(PersonUiState s) => s.UserPosed = true;

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
