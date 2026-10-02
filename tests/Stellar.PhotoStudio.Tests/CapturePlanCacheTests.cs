using System.Threading.Tasks;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

// Review 2026-10-03 (photo shapes, minors 3 + 6): the HUD pill and panel ask for the planned size every refresh — the
// framework is asked once per (shape, scale, window size); and the shown scale uses the framework's EffectiveScale
// with the GPU limit, so it always agrees with the planned size.
public sealed class CapturePlanCacheTests
{
    private sealed class FakeCapture : IScreenCapture
    {
        public int Plans;
        public (int W, int H) Screen = (1920, 1080);
        public int MaxTextureSize { get; set; } = 16384;
        public bool IsCapturing => false;
        public Task<CaptureResult> CaptureAsync(CaptureRequest request) => Task.FromResult(CaptureResult.Fail("no"));
        public CaptureSize PlanSize(CaptureRequest r)
        {
            Plans++;
            return CaptureSizing.OutputSize(Screen.W, Screen.H, r.Scale, r.Aspect, MaxTextureSize);
        }
    }

    [Fact]
    public void Asks_the_framework_once_per_shape_scale_and_window()
    {
        var f = new FakeCapture();
        var c = new CapturePlanCache(f);
        for (var i = 0; i < 5; i++) Assert.Equal(new CaptureSize(2160, 3840), c.Size(PhotoShape.Portrait9x16, 2, 1920, 1080));
        _ = c.EffectiveScale(PhotoShape.Portrait9x16, 2, 1920, 1080);
        Assert.Equal(1, f.Plans);
        c.Size(PhotoShape.Square, 2, 1920, 1080);
        c.Size(PhotoShape.Square, 4, 1920, 1080);
        f.Screen = (2560, 1440);                                   // same shape + scale, the window was resized (64 MP cap)
        Assert.Equal(new CaptureSize(8000, 8000), c.Size(PhotoShape.Square, 4, 2560, 1440));
        Assert.Equal(4, f.Plans);
    }

    [Fact]
    public void Effective_scale_includes_the_gpu_limit_and_matches_the_planned_size()
    {
        var f = new FakeCapture { MaxTextureSize = 4096 };
        var c = new CapturePlanCache(f);
        var scale = c.EffectiveScale(PhotoShape.Screen, 4, 1920, 1080);
        Assert.Equal(2, scale);                                   // 4× = 7680 > 4096 → 2× = 3840
        Assert.Equal(new CaptureSize(1920 * scale, 1080 * scale), c.Size(PhotoShape.Screen, 4, 1920, 1080));
    }
}
