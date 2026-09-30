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
        public event Action<bool>? CutsceneChanged;

        public void RaiseEntered(PhotoModeKind kind) { IsActive = true; Kind = kind; Entered?.Invoke(kind); }
        public void RaiseExited() { IsActive = false; Kind = PhotoModeKind.None; Exited?.Invoke(); }
        public void RaiseCutsceneChanged(bool v) { InCutscene = v; CutsceneChanged?.Invoke(v); }
    }

    // Task 15 fix round item 2: PhotoModeAttach and the panel hotkey (TogglePanel) must go through the SAME
    // "set view open" method so panel-open state has one writer. This test stands in for Plugin's real
    // SetViewOpen/TogglePanel (which need a full IPluginServices to construct) by composing the same three
    // real pieces (PhotoModeAttach, LookController, a view that tracks IsOpen honestly) around a local delegate
    // that mirrors Plugin.SetViewOpen exactly.
    [Fact]
    public void Game_photo_mode_opens_the_view_then_a_toggle_hotkey_closes_it()
    {
        var view = new NoOpStudioView();
        var look = new LookController(new FakeLook());
        void SetViewOpen(bool open) // == Plugin.SetViewOpen
        {
            if (open) view.Open(); else view.Close();
            look.SetPanelOpen(view.IsOpen);
        }
        var photoMode = new FakePhotoMode();
        using var attach = new PhotoModeAttach(photoMode, look, SetViewOpen);

        photoMode.RaiseEntered(PhotoModeKind.CameraFrame);
        Assert.True(view.IsOpen);

        // Shift+F10 (TogglePanel) toggles based on the view's OWN current state, through the same shared method.
        SetViewOpen(!view.IsOpen);
        Assert.False(view.IsOpen);
    }

    [Fact]
    public void Dispose_unsubscribes_both_events()
    {
        var view = new NoOpStudioView();
        var look = new LookController(new FakeLook());
        var opens = 0;
        void SetViewOpen(bool open) { opens++; if (open) view.Open(); else view.Close(); }
        var photoMode = new FakePhotoMode();
        var attach = new PhotoModeAttach(photoMode, look, SetViewOpen);
        attach.Dispose();

        photoMode.RaiseEntered(PhotoModeKind.CameraFrame);
        photoMode.RaiseExited();

        Assert.Equal(0, opens);
    }
}
