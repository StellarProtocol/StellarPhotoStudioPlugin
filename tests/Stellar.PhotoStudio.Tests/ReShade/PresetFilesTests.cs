using System;
using System.IO;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

public sealed class PresetFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ps-presetfiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void WriteNew_creates_the_folder_and_never_overwrites_a_file()
    {
        var f = new DiskPresetFiles();
        var path = Path.Combine(_dir, "presets", "Soft anime.ini");
        Assert.True(f.WriteNew(path, "Techniques=A@A.fx\n"));
        Assert.False(f.WriteNew(path, "something else"));   // edits are never lost
        Assert.Equal("Techniques=A@A.fx\n", f.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(f.Exists(path));
    }

    [Fact]
    public void Text_round_trips_byte_for_byte()
    {
        var f = new DiskPresetFiles();
        var path = Path.Combine(_dir, "x.ini");
        var text = (char)0xFEFF + "Key=1\r\nB=\"é\"\n";   // a BOM char + CRLF + non-ASCII
        Assert.True(f.WriteNew(path, text));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(path)[..3]);
        Assert.Equal(text, f.ReadAllText(path));
    }
}
