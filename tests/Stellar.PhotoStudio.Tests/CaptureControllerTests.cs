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
        var json = CaptureController.SidecarJson(CaptureResult.Ok("/p/a.png", 7680, 4320), "Cinematic", "Asterleeds", 4, new LookSettings { Dof = new DofLook { FocusDistance = 3 } });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Cinematic", doc.RootElement.GetProperty("preset").GetString());
        Assert.Equal("Asterleeds", doc.RootElement.GetProperty("map").GetString());
        Assert.Equal(7680, doc.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(3f, doc.RootElement.GetProperty("dofFocus").GetSingle());
        Assert.Equal(4, doc.RootElement.GetProperty("scale").GetInt32());
    }
}
