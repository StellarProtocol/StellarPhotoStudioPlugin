using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;

namespace Stellar.PhotoStudio.Tests.FreeCam;

internal sealed class FakeHandle : IDisposable
{
    public int Disposed;
    public void Dispose() => Disposed++;
}

internal sealed class FakeControl : ICameraControl
{
    public CameraPose GamePose { get; init; } = new(new Position3D(0, 2, -6), 0f, 10f, 0f, 45f);
    public bool IsActive { get; set; } = true;
    public float Fov { get; set; } = 45f;
    public Position3D LastPosition;
    public float LastYaw, LastPitch, LastRoll;
    public int Disposed;
    /// <summary>Simulates another plugin's throwing <c>ICameraOverride.Released</c> handler propagating out of
    /// this control's own <c>Dispose()</c> (the real framework re-raises <c>Released</c> to every holder of that
    /// event from inside <c>Dispose()</c>).</summary>
    public bool ThrowOnDispose;
    public event Action<float>? Frame;
    public void SetPose(Position3D position, float yaw, float pitch, float roll) { LastPosition = position; LastYaw = yaw; LastPitch = pitch; LastRoll = roll; }
    public void RaiseFrame(float dt) => Frame?.Invoke(dt);
    public void Dispose()
    {
        Disposed++;
        IsActive = false;
        if (ThrowOnDispose) throw new InvalidOperationException("another plugin's Released handler threw");
    }
}

internal sealed class FakeCamera : ICameraOverride
{
    public bool Busy;
    public FakeControl Control = new();
    public readonly List<FakeHandle> LookAts = new();
    public bool IsOverridden => Busy;
    public event Action<CameraReleaseReason>? Released;
    public bool TryAcquire([NotNullWhen(true)] out ICameraControl? control)
    {
        control = Busy ? null : Control;
        return !Busy;
    }
    public IDisposable LookAtCamera() { var h = new FakeHandle(); LookAts.Add(h); return h; }
    public void FrameworkRelease(CameraReleaseReason reason) { Control.IsActive = false; Released?.Invoke(reason); }
}

internal sealed class FakeShieldHandle : IInputShieldHandle
{
    public readonly HashSet<StellarKeyCode> Held = new();
    public ModifierKeys Mods;
    public bool Lmb, Rmb;
    public (float X, float Y) Delta;
    public float WheelValue;
    public (float X, float Y) PointerAt = (500f, 400f);
    public int Disposed;
    public bool IsActive => Disposed == 0;
    public bool IsHeld(StellarKeyCode key) => Held.Contains(key);
    public ModifierKeys Modifiers => Mods;
    public bool IsMouseHeld(int button) => button == 0 ? Lmb : button == 1 && Rmb;
    public (float X, float Y) MouseDelta => Delta;
    public float Wheel => WheelValue;
    public (float X, float Y) Pointer => PointerAt;
    public bool Focused;
    public bool TextFieldFocused => Focused && Disposed == 0;
    public void Dispose() => Disposed++;
}

internal sealed class FakeShield : IInputShield
{
    public FakeShieldHandle Handle = new();
    public int Raised;
    public bool IsShielded => Raised > 0 && Handle.Disposed == 0;
    public IInputShieldHandle Shield() { Raised++; return Handle; }
}

internal sealed class FakeFreeze : ISceneFreeze
{
    public readonly List<FreezeToken> Tokens = new();
    public bool IsFrozen => Tokens.Exists(t => t.Disposed == 0);
    public bool HoldsPositions => IsFrozen;
    public event Action<bool>? Changed;
    public IDisposable Freeze() { var t = new FreezeToken(this); Tokens.Add(t); Changed?.Invoke(true); return t; }
    public void FrameworkReleaseAll() { foreach (var t in Tokens) t.Disposed++; Changed?.Invoke(false); }

    /// <summary>A real-service-shaped token: like the real ref-counted <c>SceneFreezeService</c>, disposing the
    /// LAST live token raises <see cref="Changed"/>(false) synchronously, from inside <c>Dispose()</c> — the
    /// reentrancy <c>FreeCamSession.EndFreeze</c> must survive. Guarded with <see cref="_raised"/> so a repeat
    /// <c>Dispose()</c> of the same (already-disposed) token never re-raises — only <see cref="Disposed"/> keeps
    /// counting, so a test can tell a single dispose from a double one.</summary>
    internal sealed class FreezeToken : IDisposable
    {
        private readonly FakeFreeze _owner;
        private bool _raised;
        public int Disposed;
        internal FreezeToken(FakeFreeze owner) => _owner = owner;
        public void Dispose()
        {
            Disposed++;
            if (_raised || _owner.IsFrozen) return;
            _raised = true;
            _owner.Changed?.Invoke(false);
        }
    }
}

internal sealed class FakeCombatState : ICombatState
{
    public bool LocalPlayerInCombat { get; set; }
    public event Action<bool>? Changed;
    public void Raise(bool on) { LocalPlayerInCombat = on; Changed?.Invoke(on); }
}

internal sealed class FakeVisibility : ISceneVisibility
{
    public readonly List<(VisibilityLayers Layers, FakeHandle Handle)> Hides = new();
    public VisibilityLayers Hidden => VisibilityLayers.None;
    public VisibilityLayers Available => (VisibilityLayers)31;
    public event Action<VisibilityLayers>? Changed { add { } remove { } }
    public IDisposable Hide(VisibilityLayers layers) { var h = new FakeHandle(); Hides.Add((layers, h)); return h; }
}

internal sealed class FakeTransforms : IEntityTransforms
{
    public readonly Dictionary<long, Position3D> Positions = new();
    public bool Throw;
    public bool TryGetTransform(EntityId id, out Position3D position, out float yawDegrees)
    {
        if (Throw) throw new InvalidOperationException("game read failed");
        yawDegrees = 0f;
        return Positions.TryGetValue(id.Value, out position);
    }
}

internal sealed class FakeSnapshot : ICombatSnapshot
{
    public bool IsAvailable => true;
    public EntityId LocalEntityId { get; set; } = new(1);
    public IReadOnlyList<SkillCooldown> LocalCooldowns => Array.Empty<SkillCooldown>();
    public IReadOnlyList<ActiveBuff> LocalBuffs => Array.Empty<ActiveBuff>();
    public long ServerNowMs => 0;
    public DateTimeOffset ServerNow => DateTimeOffset.UnixEpoch;
    public IReadOnlyList<CombatEvent> RecentEvents => Array.Empty<CombatEvent>();
}

internal sealed class FakePicker : IEntityPicker
{
    public EntityId Result = EntityId.None;
    public int Calls;
    public bool TryPickEntity(float screenX, float screenY, out EntityId entityId) { Calls++; entityId = Result; return !Result.IsNone; }
}

internal sealed class SessionRig
{
    public readonly FakeCamera Camera = new();
    public readonly FakeShield Shield = new();
    public readonly FakeFreeze Freeze = new();
    public readonly FakeCombatState Combat = new();
    public readonly FakeVisibility Visibility = new();
    public readonly FakeTransforms Transforms = new();
    public readonly FakeSnapshot Snapshot = new();
    public readonly FakePicker Picker = new();
    public readonly MemConfigSection Config = new();
    public readonly List<(FreeCamNotice Notice, CameraReleaseReason Reason)> Notices = new();
    public readonly List<string> Warnings = new();
    /// <summary>Whether the pointer is over one of Photo Studio's own windows (every hit test asks this).</summary>
    public bool OverUi;
    public int HitTests;
    /// <summary>Simulates the "?" popover: open until the session dismisses it.</summary>
    public bool ModalOpen;
    public int Dismissed;
    public FreeCamSettings Settings;
    public FreeCamSession Session;

    public SessionRig(float smoothing = 0f, IPosing? posing = null)
    {
        Transforms.Positions[1] = new Position3D(0, 0, 0);        // the local player stands at the origin
        Config.Values["freecam.smoothing"] = smoothing;
        Settings = new FreeCamSettings(Config);
        Session = new FreeCamSession(
            new FreeCamPorts(Camera, Shield, Freeze, Combat, Visibility, Transforms, Snapshot, Picker, posing),
            Settings, new FreeCamHost((n, r) => Notices.Add((n, r)), (_, _) => { HitTests++; return OverUi; }, Dismiss, Warnings.Add));
    }

    private bool Dismiss()
    {
        if (!ModalOpen) return false;
        ModalOpen = false;
        Dismissed++;
        return true;
    }

    public void Frame(float dt = 0.016f) => Camera.Control.RaiseFrame(dt);

    /// <summary>Holds <paramref name="key"/> for one frame then releases it (one press edge).</summary>
    public void Press(StellarKeyCode key)
    {
        Shield.Handle.Held.Add(key);
        Frame();
        Shield.Handle.Held.Remove(key);
        Frame();
    }
}
