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
        // Undo any permission-bit trick before the recursive delete, or it would itself fail to remove the folder.
        if (Directory.Exists(_dir)) TryRestoreWritable(_dir);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static void TryRestoreWritable(string dir)
    {
        if (!OperatingSystem.IsLinux()) return;
        try { File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        catch { /* best-effort cleanup only */ }
    }

    [Fact]
    public void WriteNew_creates_the_folder_and_never_overwrites_a_file()
    {
        var f = new DiskReShadePresetFiles();
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
        var f = new DiskReShadePresetFiles();
        var path = Path.Combine(_dir, "x.ini");
        var text = (char)0xFEFF + "Key=1\r\nB=\"é\"\n";   // a BOM char + CRLF + non-ASCII
        Assert.True(f.WriteNew(path, text));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(path)[..3]);
        Assert.Equal(text, f.ReadAllText(path));
    }

    // Review fix round 2, item 2: a unique temp name per call (not a fixed ".tmp") plus an isolated try/catch around
    // the cleanup delete, so a failure while cleaning up never hides the real error from WriteAllBytes.
    [Fact]
    public void WriteNew_propagates_the_original_error_and_leaves_no_temp_file_when_the_write_itself_fails()
    {
        if (!OperatingSystem.IsLinux()) return;   // the permission-bit trick is POSIX-specific
        var dir = Path.Combine(_dir, "presets");
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);   // traversable, not writable
        try
        {
            var f = new DiskReShadePresetFiles();
            var path = Path.Combine(dir, "Soft anime.ini");
            Assert.Throws<UnauthorizedAccessException>(() => f.WriteNew(path, "Techniques=A@A.fx\n"));
        }
        finally
        {
            TryRestoreWritable(dir);
        }
        Assert.Empty(Directory.GetFiles(dir));   // nothing half-written left behind, under either temp-name scheme
    }

    // The cleanup delete itself must be unable to hide the original error: force File.Delete to fail (the directory
    // holding the leftover temp file loses write access) and confirm TryDeleteTemp swallows it rather than throwing —
    // mirrors the framework's own PluginDownloadService.SwapDirectory pattern (an internal seam for exactly this).
    [Fact]
    public void TryDeleteTemp_swallows_a_delete_failure_instead_of_letting_it_propagate()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Path.Combine(_dir, "locked");
        Directory.CreateDirectory(dir);
        var orphan = Path.Combine(dir, "x.ini.new-abc123");
        File.WriteAllText(orphan, "partial");
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);   // can't unlink inside it now
        try
        {
            var ex = Record.Exception(() => DiskReShadePresetFiles.TryDeleteTemp(orphan));
            Assert.Null(ex);
        }
        finally
        {
            TryRestoreWritable(dir);
        }
    }
}
