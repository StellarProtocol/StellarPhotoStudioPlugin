using System;
using System.Numerics;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// The scene Photo Studio has set up (spec 2026-10-02-photo-studio-scene-stays-design.md § 1–2, 4, 8): the freeze token,
/// the freeze centre (the free camera's leash centre while frozen) and the posing lifetime. It belongs to the scene, not
/// to the free camera: leaving the free camera keeps it. It ends on <see cref="Reset"/> (the Reset scene button), on the
/// framework's own reasons (the freeze's <see cref="ISceneFreeze.Changed"/>(false) on a zone change / cutscene; posing
/// targets released by the framework) and on <see cref="Dispose"/> (Photo Studio unloading). Main thread.
/// </summary>
internal sealed class StudioScene : IDisposable
{
    private readonly ISceneFreeze _freeze;
    private readonly IPosing? _posing;
    private readonly Action<bool> _onFreezeChanged;
    private Func<int> _posedCount = () => 0;
    private IDisposable? _token;
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

    /// <summary>Anything set up: frozen, or anyone posed. While set, re-entering the free camera returns it to its last
    /// pose (spec § 6) and the off-camera SCENE pill shows (spec § 7).</summary>
    public bool IsSet => Frozen || PosedCount > 0;

    /// <summary>Wires the posed-people count (the Person group's controller); called once at start.</summary>
    public void TrackPoses(Func<int> posedCount) => _posedCount = posedCount;

    /// <summary>The Person group's posed set changed; forwards one <see cref="Changed"/>.</summary>
    public void NotifyPosesChanged() => Changed?.Invoke();

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
        Changed?.Invoke();
    }

    /// <summary>A newly selected person becomes the freeze centre while frozen (the free camera's existing rule).</summary>
    public void MoveFreezeCentre(Vector3 centre)
    {
        if (_token is not null) FreezeCentre = centre;
    }

    /// <summary>The Reset scene button: ends the freeze and returns every posed person to normal (copies removed, real
    /// players shown again).</summary>
    public void Reset()
    {
        EndFreeze();
        _posing?.ResetAll();
        Changed?.Invoke();
    }

    /// <summary>Spec (free camera) § 7, kept: the local player's death ends the freeze.</summary>
    public void OnLocalDeath()
    {
        if (_token is null) return;
        EndFreeze();
        Changed?.Invoke();
    }

    /// <summary>Photo Studio unloading ends the scene.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _freeze.Changed -= _onFreezeChanged;
        EndFreeze();
        _posing?.ResetAll();
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
        Changed?.Invoke();
    }
}
