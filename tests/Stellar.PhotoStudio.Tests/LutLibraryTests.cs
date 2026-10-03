using System;
using System.IO;
using System.Linq;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class LutLibraryTests
{
    private static byte[] PngHeader(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 }.CopyTo(b, 0);
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }

    [Theory]
    [InlineData(256, 16, true)] [InlineData(1024, 32, true)] [InlineData(512, 16, false)] [InlineData(256, 256, false)]
    public void Validates_strip_dimensions(int w, int h, bool ok) => Assert.Equal(ok, LutLibrary.Validate(PngHeader(w, h)));

    [Fact]
    public void Rejects_non_png() => Assert.False(LutLibrary.Validate(new byte[] { 1, 2, 3 }));

    [Fact]
    public void List_matches_png_extension_case_insensitively_on_every_os()
    {
        var dir = Directory.CreateTempSubdirectory("photostudio-lut-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "a.png"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(dir, "B.PNG"), new byte[] { 0 });
            File.WriteAllBytes(Path.Combine(dir, "c.txt"), new byte[] { 0 });

            var list = LutLibrary.List(dir);

            Assert.Equal(2, list.Count);
            Assert.Contains(list, p => Path.GetFileName(p) == "a.png");
            Assert.Contains(list, p => Path.GetFileName(p) == "B.PNG");
            Assert.DoesNotContain(list, p => Path.GetFileName(p) == "c.txt");
            // sorted ordinal-ignore-case
            Assert.Equal(list.OrderBy(p => p, StringComparer.OrdinalIgnoreCase), list);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
