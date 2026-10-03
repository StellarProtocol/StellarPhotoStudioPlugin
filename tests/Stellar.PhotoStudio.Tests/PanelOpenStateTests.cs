using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class PanelOpenStateTests
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

    /// <summary>Ignores <see cref="Close"/> while pinned — stands in for a real panel that keeps itself open
    /// when the user pinned it, the exact case <see cref="PanelOpenState"/> must handle correctly.</summary>
    private sealed class PinnedOpenView : IStudioView
    {
        public bool IsOpen { get; private set; }
        public bool PinnedOpen;
        public void Open() => IsOpen = true;
        public void Close() { if (!PinnedOpen) IsOpen = false; }
        public void ShowSavedToast(CaptureResult result) { }
        public void ShowError(string message) { }
    }

    [Fact]
    public void Set_true_opens_the_view()
    {
        var view = new NoOpStudioView();
        var panel = new PanelOpenState(view, new LookController(new FakeLook()));
        panel.Set(true);
        Assert.True(view.IsOpen);
        Assert.True(panel.IsOpen);
    }

    [Fact]
    public void Set_false_closes_the_view()
    {
        var view = new NoOpStudioView();
        var panel = new PanelOpenState(view, new LookController(new FakeLook()));
        panel.Set(true);
        panel.Set(false);
        Assert.False(view.IsOpen);
        Assert.False(panel.IsOpen);
    }

    [Fact]
    public void Toggle_flips_the_views_current_state()
    {
        var view = new NoOpStudioView();
        var panel = new PanelOpenState(view, new LookController(new FakeLook()));
        panel.Toggle();
        Assert.True(view.IsOpen);
        panel.Toggle();
        Assert.False(view.IsOpen);
    }

    // Regression pin for the bug fix-round-1 caught: driving LookController.SetPanelOpen from the REQUESTED
    // value (rather than the view's actual resulting state) desyncs the look preview the moment a concrete view
    // ignores Close() while pinned open.
    [Fact]
    public void A_pinned_view_that_ignores_close_still_drives_the_look_correctly()
    {
        var view = new PinnedOpenView { PinnedOpen = true };
        var lookBackend = new FakeLook();
        var look = new LookController(lookBackend);
        var panel = new PanelOpenState(view, look);
        look.SetDraft(new LookSettings());

        panel.Set(true);
        look.Tick();
        Assert.False(lookBackend.Log[^1]!.PlayMode); // panel open → previewing

        panel.Set(false); // the view ignores this — stays open
        look.Tick();

        Assert.True(view.IsOpen);
        Assert.False(lookBackend.Log[^1]!.PlayMode); // preview persists: SetPanelOpen followed the view's REAL state
    }
}
