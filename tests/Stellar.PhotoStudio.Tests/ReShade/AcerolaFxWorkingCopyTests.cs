using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// Root cause found in game 2026-10-04: AcerolaFX_End.fx returns lerp(AcerolaBuffer, original, original.a * _MaskUI).
// AcerolaFX was written for FFXIV, whose backbuffer alpha marks UI; this game's alpha is 1 everywhere, so with Mask UI
// on (every AcerolaFX preset ships _MaskUI=1) End restores the original frame and the whole look is discarded. CRT and
// Dither carry the same switch. The installed copy sets every _MaskUI to 0; the checked download stays byte-identical.
// Fixtures/AcerolaFX holds the REAL preset files (MIT, @ c33f779; their sha256 is checked against the catalog here) and
// the working-copy pins below were measured from them independently (Python: every column-0 "_MaskUI=" value -> 0).
public sealed class AcerolaFxWorkingCopyTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "../../../ReShade/Fixtures/AcerolaFX");

    [Theory]
    [InlineData("acerolafx-gameplay", "AcerolaFX_GameplayLowest.ini", 1, 3451L, "04a07d2443940404864339b9d2796d02c8810b452ebe27a3e2ba36aadc1594a0")]
    [InlineData("acerolafx-golden-age", "AcerolaFX_GoldenAge.ini", 1, 8210L, "255d661e883beed274819a2a743119f25805615e219d425702cc11dfca74de61")]
    [InlineData("acerolafx-draft", "AcerolaFX_Draft.ini", 1, 5746L, "10ee0c555dc82fd47bc2f2f2c359dabe824e0645ff482df04414f8f06ca2b258")]
    [InlineData("acerolafx-distant-past", "AcerolaFX_DistantPast.ini", 3, 4195L, "9872134e7dfae7189b668d2ab7b2f43cd310a8391e965cab3793ff80e94e479c")]
    public void Working_copy_turns_every_Mask_UI_off_and_matches_its_pin(string id, string file, int maskUi, long size, string sha)
    {
        var e = PresetCatalog.Find(id)!;
        var bytes = File.ReadAllBytes(Path.Combine(Fixtures, file));
        Assert.Equal(e.Sha256, Hex(bytes));   // the fixture IS the pinned download
        var source = Encoding.UTF8.GetString(bytes);
        Assert.Equal(maskUi, Count(source, "\n_MaskUI=1"));

        var copy = PresetOverrides.WorkingCopy(e, source);
        var copyBytes = Encoding.UTF8.GetBytes(copy);
        Assert.Equal(size, copyBytes.LongLength);
        Assert.Equal(sha, Hex(copyBytes));
        Assert.Equal(0, Count(copy, "_MaskUI=1"));
        Assert.Equal(maskUi, Count(copy, "\n_MaskUI=0"));
        Assert.Equal(source, PresetOverrides.PreviousWorkingCopy(e, source));   // before the fix the copy WAS the download
    }

    [Fact]
    public void AcerolaFx_entries_are_adjusted_and_set_only_Mask_UI()
    {
        foreach (var e in PresetCatalog.All)
        {
            if (e.Id.StartsWith("acerolafx-", StringComparison.Ordinal))
            {
                Assert.True(e.Adjusted);
                Assert.Equal(new[] { "_MaskUI=0" }, e.SetValues.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            }
            else Assert.Empty(e.SetValues);   // no other preset changes a value
        }
    }

    private static int Count(string s, string what)
    {
        var n = 0;
        for (var i = s.IndexOf(what, StringComparison.Ordinal); i >= 0; i = s.IndexOf(what, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    private static string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
}
