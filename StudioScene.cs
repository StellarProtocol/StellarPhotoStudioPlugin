using System;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// The scene Photo Studio has set up (spec 2026-10-02-photo-studio-scene-stays-design.md § 1–2, 4, 8): the freeze token,
/// the freeze centre (the free camera's leash centre while frozen) and the posing lifetime. It belongs to the scene, not
/// to the free camera: leaving the free camera keeps it; the lights (Lights tab) belong to it too. It ends on <see cref="Reset"/> (the Reset scene button), on the
/// framework's own reasons (the freeze's <see cref="ISceneFreeze.Changed"/>(false) on a zone change / cutscene; posing
/// targets released by the framework) and on <see cref="Dispose"/> (Photo Studio unloading). While frozen or posed
/// (<see cref="KeepsCamera"/>) it also keeps the free camera's entry hides once the camera has left (spec § 1, review I-1:
/// <see cref="KeepEntryHide"/> / <see cref="TakeEntryHide"/>) and releases them when that ends; each end bumps
/// <see cref="Generation"/> so a pose remembered for an older scene is never restored (review I-3). Lamps alone make the
/// scene set (<see cref="IsSet"/>: the SCENE pill, Reset scene) but keep neither the hides nor the camera pose (owner
/// ruling 2026-10-03, "Come back").
/// Main thread.
/// </summary>
internal sealed class StudioScene : IDisposable
{
    private readonly ISceneFreeze _freeze;
    private readonly IPosing? _posing;
    private readonly Action<bool> _onFreezeChanged;
    private Func<int> _posedCount = () => 0;
    private Func<int> _lightsCount = () => 0;
    private Action _clearLights = () => { };
    private IDisposable? _token;
    private IDisposable? _entryHide;
    private VisibilityLayers _entryHideLayers;
    private bool _wasKept;
    private bool _disposed;

    public StudioScene(ISceneFreeze freeze, IPosing? posing)
    {
        _freeze = freeze;
        _posing = posing;
        _onFreezeChanged = OnFreezeChanged;
        _freeze.Changed += _onFreezeChanged;
    }

    /// <summary>Raised (main thread) whenever <see cref="Frozen"/>, <see cref="PosedCount"/> or <see cref="IsSet"/> may
    /// have changed.</summary>
    public event Action? Changed;

    /// <summary>True while Photo Studio holds a scene freeze.</summary>
    public bool Frozen => _token is not null;

    /// <summary>Where the scene was frozen around (the free camera's leash centre while frozen); null when not frozen,
    /// or when frozen with no position known.</summary>
    public Vector3? FreezeCentre { get; private set; }

    /// <summary>How many people are posed (touched through the Person group and not yet reset or released).</summary>
    public int PosedCount => _posedCount();

    /// <summary>Anything set up: frozen, anyone posed, or any light. While set, the off-camera SCENE pill shows (spec § 7)
    /// and Reset scene has something to reset.</summary>
    public bool IsSet => KeepsCamera || LightsCount > 0;

    /// <summary>Frozen or anyone posed: while true the scene keeps the free camera's entry hides after it leaves and
    /// re-entry returns the camera to its last pose (spec § 6). Lamps alone keep only the lights (owner ruling 2026-10-03,
    /// "Come back"): the hides come back and re-entry starts from the game camera.</summary>
    public bool KeepsCamera => Frozen || PosedCount > 0;

    /// <summary>Lamps + lit people (lights spec § 4: lights belong to the scene).</summary>
    public int LightsCount => _lightsCount();

    /// <summary>Counts scene ends (set → not set, Reset scene, unload). A free-camera pose remembered under an older
    /// generation belongs to a scene that is gone, so re-entry starts from the game camera (review I-3).</summary>
    public int Generation { get; private set; }

    /// <summary>True while the scene keeps the free camera's entry hides (the camera left with the scene set).</summary>
    public bool HoldsEntryHide => _entryHide is not null;

    /// <summary>The layers the kept entry hide covers (<see cref="VisibilityLayers.None"/> when none is kept).</summary>
    public VisibilityLayers EntryHideLayers => _entryHide is null ? VisibilityLayers.None : _entryHideLayers;

    /// <summary>Wires the posed-people count (the Person group's controller); called once at start.</summary>
    public void TrackPoses(Func<int> posedCount) => _posedCount = posedCount;

    /// <summary>Wires the lights (the Lights tab's controller): their count makes the scene set; Reset scene and unload
    /// clear them. Called once at start.</summary>
    public void TrackLights(Func<int> count, Action clear)
    {
        _lightsCount = count;
        _clearLights = clear;
    }

    /// <summary>The lights changed (added, removed, or ended by the framework); forwards one <see cref="Changed"/>.</summary>
    public void NotifyLightsChanged() => Raise();

    /// <summary>The Person group's posed set changed; forwards one <see cref="Changed"/>.</summary>
    public void NotifyPosesChanged() => Raise();

    /// <summary>The free camera left while the scene is set: the scene keeps its entry hide until the scene ends (spec
    /// § 1). With nothing set (or once disposed) the hide is released at once — nothing would ever release it later.</summary>
    public void KeepEntryHide(IDisposable hide, VisibilityLayers layers)
    {
        if (_disposed || !KeepsCamera)
        {
            hide.Dispose();
            return;
        }
        var old = _entryHide;
        (_entryHide, _entryHideLayers) = (hide, layers);
        _wasKept = true;
        if (!ReferenceEquals(old, hide)) old?.Dispose();
    }

    /// <summary>The free camera re-enters: hands back the kept entry hide (null when none), so the camera reuses it instead
    /// of stacking a second hide of the same layers.</summary>
    public IDisposable? TakeEntryHide(out VisibilityLayers layers)
    {
        var hide = _entryHide;
        layers = hide is null ? VisibilityLayers.None : _entryHideLayers;
        _entryHide = null;
        return hide;
    }

    /// <summary>Freezes the scene around <paramref name="centre"/>, or ends the freeze when already frozen.</summary>
    public void ToggleFreeze(Vector3? centre)
    {
        if (_disposed) return;
        if (_token is not null) EndFreeze();
        else
        {
            _token = _freeze.Freeze();
            FreezeCentre = centre;
        }
        Raise();
    }

    /// <summary>A newly selected person becomes the freeze centre while frozen (the free camera's existing rule).</summary>
    public void MoveFreezeCentre(Vector3 centre)
    {
        if (_token is not null) FreezeCentre = centre;
    }

    /// <summary>The Reset scene button: ends the freeze, removes the lights and returns every posed person to normal
    /// (copies removed, real players shown again). The lights go BEFORE the posing reset (lights review I-3): a lit posed
    /// copy is written back while it still exists.</summary>
    public void Reset()
    {
        if (_disposed) return;
        EndFreeze();
        var hadLights = LightsCount > 0;
        _clearLights();
        _posing?.ResetAll();
        if (_wasKept || _entryHide is not null || hadLights) EndScene();
        _wasKept = KeepsCamera;
        Changed?.Invoke();
    }

    /// <summary>Spec (free camera) § 7, kept: the local player's death ends the freeze.</summary>
    public void OnLocalDeath()
    {
        if (_token is null) return;
        EndFreeze();
        Raise();
    }

    /// <summary>Photo Studio unloading ends the scene.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _freeze.Changed -= _onFreezeChanged;
        EndFreeze();
        _clearLights();      // before the posing reset (review I-3): lit copies are written back while they exist
        _posing?.ResetAll();
        EndScene();
    }

    /// <summary>Null the field before disposing: the real SceneFreezeService is ref-counted and raises
    /// <see cref="ISceneFreeze.Changed"/>(false) synchronously when the last token is disposed, re-entering
    /// <see cref="OnFreezeChanged"/> while this method is still on the stack — with the field already null, that
    /// re-entry sees nothing to end and just forwards the one change notification, instead of disposing the same token a
    /// second time. Raises nothing itself (the public callers do), so the reentrancy can be pinned directly.</summary>
    internal void EndFreeze()
    {
        var token = _token;
        _token = null;
        FreezeCentre = null;
        token?.Dispose();
    }

    private void OnFreezeChanged(bool frozen)
    {
        if (!frozen && _token is not null) EndFreeze();   // the framework unfroze (zone change / cutscene)
        Raise();
    }

    /// <summary>Every change notification goes through here: the camera-keeping part ending (frozen / posed → neither)
    /// ends the scene's camera state — the remembered pose goes stale and the kept entry hides are released — even while
    /// lamps remain (owner ruling 2026-10-03).</summary>
    private void Raise()
    {
        var kept = KeepsCamera;
        if (_wasKept && !kept) EndScene();
        _wasKept = kept;
        Changed?.Invoke();
    }

    /// <summary>The scene ended: a new generation (the remembered camera pose is stale) and the kept entry hides go.</summary>
    private void EndScene()
    {
        Generation++;
        _wasKept = false;
        var hide = _entryHide;
        _entryHide = null;
        hide?.Dispose();
    }
}
