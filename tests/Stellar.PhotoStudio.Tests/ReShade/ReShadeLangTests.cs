using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// R9: every ReShade string in en/ja/th/id/fil with the English placeholders.
public sealed class ReShadeLangTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "../../../../../Lang");

    private static readonly string[] Keys =
    {
        "ps.look.reshade", "ps.help.look.reshade", "ps.rs.notInstalled", "ps.rs.unreachable", "ps.rs.loading", "ps.rs.use",
        "ps.help.rs.use", "ps.rs.preset", "ps.rs.presetOther", "ps.rs.savedWithLook", "ps.rs.effectsOn", "ps.rs.allEffects",
        "ps.rs.noEffects", "ps.rs.depthTag", "ps.rs.depthSkipped", "ps.rs.depthDetail", "ps.rs.fineTune", "ps.rs.packs",
        "ps.rs.packsFrom", "ps.help.rs.packs", "ps.help.rs.pack", "ps.rs.pack.installed", "ps.rs.pack.download",
        "ps.rs.pack.update", "ps.rs.pack.retry", "ps.rs.pack.queued", "ps.rs.pack.mixed",
        "ps.rs.status.on", "ps.rs.status.applying", "ps.rs.note.notReady", "ps.rs.reloading", "ps.rs.pack.failedDep", "ps.rs.savedOnOff", "ps.rs.moreFx",
    };

    [Theory]
    [InlineData("en")] [InlineData("ja")] [InlineData("th")] [InlineData("id")] [InlineData("fil")]
    public void Every_reshade_key_exists_with_the_english_placeholders(string code)
    {
        var en = Load("en");
        var cat = Load(code);
        foreach (var key in Keys)
        {
            Assert.True(cat.TryGetProperty(key, out var v), $"{code} lacks {key}");
            Assert.False(string.IsNullOrWhiteSpace(v.GetString()), $"{code}.{key} is empty");
            for (var i = 0; i < 4; i++)
                Assert.Equal(en.GetProperty(key).GetString()!.Contains("{" + i + "}"), v.GetString()!.Contains("{" + i + "}"));
        }
    }

    // R3 wording pin: the not-installed line points to Photo Studio's page in the launcher.
    [Fact]
    public void Not_installed_line_points_to_the_launcher_page()
    {
        var s = Load("en").GetProperty("ps.rs.notInstalled").GetString()!;
        Assert.Contains("Stellar launcher", s);
        Assert.Contains("Photo Studio's page", s);
    }

    private static JsonElement Load(string code) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, code + ".json"))).RootElement;
}
