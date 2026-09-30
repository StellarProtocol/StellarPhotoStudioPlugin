using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class PhotoModeAttachTests
{
    private sealed class FakeLook : IRenderLook
    {
        public readonly List<LookSettings?> Log = new();
        public LookCapabilities Capabilities => new((LookGroups)127);
        public ILookHandle Apply(LookSettings s) { Log.Add(s); return new H(this); }
        private sealed class H : ILookHandle
        {
            private FakeLook? _o; public H(FakeLook o) => _o = o;
            public bool IsActive => _o is not null;
            public void Update(LookSettings s) => _o?.Log.Add(s);
            public void Dispose() { _o?.Log.Add(null); _o = null; }
        }
    }

    private sealed class FakePhotoMode : IPhotoModeState
    {
        public bool IsActive { get; private set; }
        public PhotoModeKind Kind { get; private set; }
        public event Action<PhotoModeKind>? Entered;
        public event Action? Exited;
        public bool InCutscene { get; set; }
        public event Action<bool>? CutsceneChanged { add { } remove { } }

        public void RaiseEntered(PhotoModeKind kind) { IsActive = true; Kind = kind; Entered?.Invoke(kind); }
        public void RaiseExited() { IsActive = false; Kind = PhotoModeKind.None; Exited?.Invoke(); }
    }

    [Fact]
    public void Game_photo_mode_entry_and_exit_are_reported_to_the_strip_owner()
    {
        var docked = new List<bool>();
        var photoMode = new FakePhotoMode();
        using var attach = new PhotoModeAttach(photoMode, docked.Add);

        photoMode.RaiseEntered(PhotoModeKind.Selfie);
        photoMode.RaiseExited();

        Assert.Equal(new[] { true, false }, docked);
    }

    [Fact]
    public void Dispose_unsubscribes_both_events()
    {
        var docked = new List<bool>();
        var photoMode = new FakePhotoMode();
        var attach = new PhotoModeAttach(photoMode, docked.Add);
        attach.Dispose();

        photoMode.RaiseEntered(PhotoModeKind.CameraFrame);
        photoMode.RaiseExited();

        Assert.Empty(docked);
    }
}
