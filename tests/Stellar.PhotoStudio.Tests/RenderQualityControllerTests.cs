using System;
using System.Collections.Generic;
using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

public sealed class RenderQualityControllerTests
{
    private sealed class FakeQuality : IRenderQuality
    {
        public readonly List<(RenderQualityRequest Req, bool Live)> Tokens = new();
        public IDisposable Request(RenderQualityRequest request)
        {
            var i = Tokens.Count;
            Tokens.Add((request, true));
            return new D(() => Tokens[i] = (Tokens[i].Req, false));
        }
        public IEnumerable<RenderQualityRequest> Live_ => Tokens.Where(t => t.Live).Select(t => t.Req);
        public RenderQualityState Live => default;
        public RenderQualityCapabilities Capabilities => new(true, true, true);
    }

    private sealed class FakeTime : ITimeOfDay
    {
        public readonly List<Pin_> Pins = new();
        public bool IsAvailable { get; set; } = true;
        public float CurrentHour => 0f;
        public ITimePin Pin(float hour) { var p = new Pin_ { Hour = hour }; Pins.Add(p); return p; }
        public sealed class Pin_ : ITimePin
        {
            public float Hour; public bool IsActive { get; private set; } = true;
            public void SetHour(float hour) => Hour = hour;
            public void Dispose() => IsActive = false;
        }
    }

    private sealed class D : IDisposable { private Action? _a; public D(Action a) => _a = a; public void Dispose() { _a?.Invoke(); _a = null; } }

    [Fact]
    public void While_composing_holds_only_while_the_panel_is_open()
    {
        var q = new FakeQuality(); var c = new RenderQualityController(q, new FakeTime());
        c.SetSupersample(QualityMode.WhileComposing);
        Assert.Empty(q.Live_);
        c.SetComposing(true);
        Assert.True(q.Live_.Single().Supersample);
        c.SetComposing(false);
        Assert.Empty(q.Live_);
    }

    [Fact]
    public void Always_holds_regardless_of_the_panel()
    {
        var q = new FakeQuality(); var c = new RenderQualityController(q, new FakeTime());
        c.SetShadows(QualityMode.Always);
        Assert.True(q.Live_.Single().HighShadows);
    }

    [Fact]
    public void Changing_what_is_asked_takes_the_new_token_before_releasing_the_old()
    {
        var q = new FakeQuality(); var c = new RenderQualityController(q, new FakeTime());
        c.SetSupersample(QualityMode.Always);
        c.SetShadows(QualityMode.Always);
        var live = q.Live_.ToList();
        Assert.Single(live);
        Assert.True(live[0].Supersample && live[0].HighShadows);
        Assert.Equal(2, q.Tokens.Count);   // no gap: the second request existed before the first was released
    }

    [Fact]
    public void An_unchanged_request_is_not_re_requested()
    {
        var q = new FakeQuality(); var c = new RenderQualityController(q, new FakeTime());
        c.SetSupersample(QualityMode.Always);
        c.SetComposing(true);
        c.SetComposing(false);
        Assert.Single(q.Tokens);
    }

    [Fact]
    public void Boost_for_capture_raises_shadows_only_during_the_capture()
    {
        var q = new FakeQuality(); var c = new RenderQualityController(q, new FakeTime());
        c.SetCapturing(true);
        Assert.True(c.BoostingForCapture);
        Assert.True(q.Live_.Single().HighShadows);
        c.SetCapturing(false);
        Assert.Empty(q.Live_);
        c.SetBoostForCapture(false);
        c.SetCapturing(true);
        Assert.Empty(q.Live_);
    }

    [Fact]
    public void Time_pin_follows_the_mode_and_the_hour_updates_the_live_pin()
    {
        var t = new FakeTime(); var c = new RenderQualityController(new FakeQuality(), t);
        c.SetHour(18f);
        c.SetTime(QualityMode.Always);
        Assert.Equal(18f, t.Pins.Single().Hour);
        c.SetHour(6.5f);
        Assert.Equal(6.5f, t.Pins.Single().Hour);
        c.SetTime(QualityMode.Off);
        Assert.False(t.Pins.Single().IsActive);
    }

    [Fact]
    public void Time_is_not_pinned_when_the_client_cannot()
    {
        var t = new FakeTime { IsAvailable = false }; var c = new RenderQualityController(new FakeQuality(), t);
        c.SetTime(QualityMode.Always);
        Assert.Empty(t.Pins);
    }

    [Fact]
    public void Dispose_releases_everything()
    {
        var q = new FakeQuality(); var t = new FakeTime(); var c = new RenderQualityController(q, t);
        c.Configure(QualityMode.Always, QualityMode.Always, QualityMode.Always, 9f, true);
        c.Dispose();
        Assert.Empty(q.Live_);
        Assert.False(t.Pins.Single().IsActive);
    }
}
