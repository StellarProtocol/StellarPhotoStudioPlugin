using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class LookControllerTests
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

    private static readonly LookSettings Look = new() { Color = new ColorLook { Saturation = -50 }, Dof = new DofLook() };

    [Fact]
    public void Panel_open_applies_draft_in_photo_mode()
    {
        var f = new FakeLook(); var c = new LookController(f);
        c.SetDraft(Look); c.SetPanelOpen(true); c.Tick();
        Assert.False(f.Log[^1]!.PlayMode);
    }

    [Fact]
    public void Closing_without_pin_disposes()
    {
        var f = new FakeLook(); var c = new LookController(f);
        c.SetDraft(Look); c.SetPanelOpen(true); c.Tick();
        c.SetPanelOpen(false); c.Tick();
        Assert.Null(f.Log[^1]);
    }

    [Fact]
    public void Closing_with_pin_switches_to_play_mode()
    {
        var f = new FakeLook(); var c = new LookController(f);
        c.SetDraft(Look); c.SetPinned(true); c.SetPanelOpen(true); c.Tick();
        c.SetPanelOpen(false); c.Tick();
        Assert.True(f.Log[^1]!.PlayMode);
    }

    [Fact]
    public void Suspended_pinned_look_is_removed_then_restored()
    {
        var f = new FakeLook(); var c = new LookController(f);
        c.SetDraft(Look); c.SetPinned(true); c.Tick();
        c.SetSuspended(true); c.Tick();
        Assert.Null(f.Log[^1]);
        c.SetSuspended(false); c.Tick();
        Assert.True(f.Log[^1]!.PlayMode);
    }

    [Fact]
    public void Many_draft_changes_in_one_frame_push_once()
    {
        var f = new FakeLook(); var c = new LookController(f);
        c.SetPanelOpen(true); c.SetDraft(Look); c.Tick();
        var n = f.Log.Count;
        for (var i = 0; i < 10; i++) c.SetDraft(Look with { Color = new ColorLook { Saturation = i } });
        c.Tick();
        Assert.Equal(n + 1, f.Log.Count);
    }

    [Fact]
    public void Dispose_releases_the_handle()
    {
        var f = new FakeLook(); var c = new LookController(f);
        c.SetDraft(Look); c.SetPanelOpen(true); c.Tick();
        c.Dispose();
        Assert.Null(f.Log[^1]);
    }
}
