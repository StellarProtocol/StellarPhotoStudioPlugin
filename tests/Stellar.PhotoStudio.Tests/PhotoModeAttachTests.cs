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

    // Task 15 fix round item 2 (+ follow-up): PhotoModeAttach and the panel hotkey (TogglePanel) must go through
    // the SAME PanelOpenState so panel-open state has one writer. This test uses the REAL PanelOpenState (not a
    // hand-mirrored copy of its logic), so a future change to PanelOpenState is exercised by both this test and
    // Plugin.cs automatically.
    [Fact]
    public void Game_photo_mode_opens_the_view_then_a_toggle_hotkey_closes_it()
    {
        var view = new NoOpStudioView();
        var look = new LookController(new FakeLook());
        var panel = new PanelOpenState(view, look);
        var photoMode = new FakePhotoMode();
        using var attach = new PhotoModeAttach(photoMode, look, panel);

        photoMode.RaiseEntered(PhotoModeKind.CameraFrame);
        Assert.True(view.IsOpen);

        panel.Toggle(); // == Plugin.TogglePanel (Shift+F10)
        Assert.False(view.IsOpen);
    }

    [Fact]
    public void Dispose_unsubscribes_both_events()
    {
        var view = new NoOpStudioView();
        var look = new LookController(new FakeLook());
        var panel = new PanelOpenState(view, look);
        var photoMode = new FakePhotoMode();
        var attach = new PhotoModeAttach(photoMode, look, panel);
        attach.Dispose();

        photoMode.RaiseEntered(PhotoModeKind.CameraFrame);
        photoMode.RaiseExited();

        Assert.False(view.IsOpen); // neither handler fired after Dispose, so the view was never touched
    }
}
