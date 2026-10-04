using System;
using Stellar.Abstractions.Domain;
using Xunit;
namespace Stellar.PhotoStudio.Tests;

public sealed class CaptureControllerTests
{
    [Fact]
    public void Builds_timestamped_request_in_default_folder()
    {
        var r = CaptureController.BuildRequest(new CaptureSettings(2, CaptureFormat.Png, 92, null, VisibilityLayers.GameHud),
            new DateTime(2026, 9, 30, 14, 32, 5), "/g/stellar/screenshots");
        Assert.Equal("BPSR_2026-09-30_14-32-05", r.FileStem);
        Assert.Equal("/g/stellar/screenshots", r.Directory);
        Assert.Equal(VisibilityLayers.GameHud, r.HideDuringCapture);
    }

    [Fact]
    public void Custom_folder_wins() =>
        Assert.Equal("/pics", CaptureController.BuildRequest(new CaptureSettings(1, CaptureFormat.Jpg, 80, "/pics", 0), DateTime.Now, "/d").Directory);

    [Fact]
    public void Sidecar_contains_preset_map_and_scale()
    {
        var json = CaptureController.SidecarJson(CaptureResult.Ok("/p/a.png", 7680, 4320),
            new SidecarInfo("Cinematic", "Asterleeds", 4, new LookSettings { Dof = new DofLook { FocusDistance = 3 } }, null));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Cinematic", doc.RootElement.GetProperty("preset").GetString());
        Assert.Equal("Asterleeds", doc.RootElement.GetProperty("map").GetString());
        Assert.Equal(7680, doc.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(3f, doc.RootElement.GetProperty("dofFocus").GetSingle());
        Assert.Equal(4, doc.RootElement.GetProperty("scale").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, doc.RootElement.GetProperty("reshade").ValueKind);
    }

    [Fact]
    public void Sidecar_records_the_reshade_preset_version_and_notes()
    {
        var shot = new ReShadeShot("Golden hour.ini", "6.8.0.2155", Applied: true, Array.Empty<string>());
        var json = CaptureController.SidecarJson(CaptureResult.Ok("/p/a.png", 1920, 1080), new SidecarInfo("Natural", "m", 1, null, shot));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var rs = doc.RootElement.GetProperty("reshade");
        Assert.Equal("Golden hour.ini", rs.GetProperty("preset").GetString());
        Assert.Equal("6.8.0.2155", rs.GetProperty("version").GetString());
        Assert.True(rs.GetProperty("applied").GetBoolean());
        Assert.Equal(0, rs.GetProperty("notes").GetArrayLength());
    }

    [Fact]
    public void Sidecar_marks_a_photo_taken_without_reshade()
    {
        var shot = new ReShadeShot("Noir.ini", null, Applied: false, new[] { "ReShade was not ready — photo taken without it." });
        var json = CaptureController.SidecarJson(CaptureResult.Ok("/p/a.png", 1, 1), new SidecarInfo("Natural", "m", 1, null, shot));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var rs = doc.RootElement.GetProperty("reshade");
        Assert.False(rs.GetProperty("applied").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, rs.GetProperty("version").ValueKind);
        Assert.Equal("ReShade was not ready — photo taken without it.", rs.GetProperty("notes")[0].GetString());
    }
}
