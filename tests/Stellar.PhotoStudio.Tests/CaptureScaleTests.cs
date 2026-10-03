using Xunit;

namespace Stellar.PhotoStudio.Tests;

public sealed class CaptureScaleTests
{
    [Theory]
    [InlineData(1920, 1080, 4, 4)]   // 7680×4320 = 33 MP
    [InlineData(2560, 1440, 4, 4)]   // 10240×5760 = 59 MP, under both caps
    [InlineData(3840, 2160, 4, 2)]   // 4K at 4× = 133 MP → capped
    [InlineData(3440, 1440, 4, 2)]   // ultrawide at 4× = 79 MP → capped
    [InlineData(5120, 1440, 4, 2)]   // long side 20480 → capped
    [InlineData(1920, 1080, 2, 2)]
    [InlineData(1920, 1080, 1, 1)]
    public void Lowers_the_scale_until_both_caps_fit(int w, int h, int requested, int expected)
        => Assert.Equal(expected, CaptureScale.Effective(w, h, requested));
}
