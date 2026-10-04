using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// Every Presets string in en/ja/th/id/fil with the English placeholders {0}..{5}.
public sealed class PresetLangTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "../../../../../Lang");

    private static readonly string[] Keys =
    {
        "ps.rs.presets", "ps.rs.presets.hint", "ps.help.rs.presets", "ps.rs.preset.ours", "ps.rs.preset.uses",
        "ps.rs.preset.byLine", "ps.rs.preset.coverage", "ps.rs.preset.downloading", "ps.rs.preset.linkLine",
        "ps.rs.preset.copyLink", "ps.rs.preset.copied", "ps.rs.preset.why.noLicence", "ps.rs.preset.why.ask",
        "ps.help.rs.preset.community", "ps.help.rs.preset.partial", "ps.help.rs.preset.adjusted", "ps.help.rs.preset.link",
        "ps.help.rs.preset.own", "ps.rs.presets.import", "ps.rs.preset.desc.cinematic-warm", "ps.rs.preset.desc.soft-anime",
        "ps.rs.preset.desc.cool-night", "ps.rs.preset.desc.clean-sharpen",
        "ps.rs.presets.community", "ps.rs.presets.links", "ps.rs.fail.changed", "ps.rs.fail.network", "ps.rs.fail.other",
        "ps.help.rs.lastError",
    };

    [Theory]
    [InlineData("en")] [InlineData("ja")] [InlineData("th")] [InlineData("id")] [InlineData("fil")]
    public void Every_preset_key_exists_with_the_english_placeholders(string code)
    {
        var en = Load("en");
        var cat = Load(code);
        foreach (var key in Keys)
        {
            Assert.True(cat.TryGetProperty(key, out var v), $"{code} lacks {key}");
            Assert.False(string.IsNullOrWhiteSpace(v.GetString()), $"{code}.{key} is empty");
            for (var i = 0; i < 6; i++)
                Assert.Equal(en.GetProperty(key).GetString()!.Contains("{" + i + "}"), v.GetString()!.Contains("{" + i + "}"));
        }
    }

    [Fact]
    public void The_own_badge_is_the_brand_name_in_every_locale()
    {
        foreach (var code in new[] { "en", "ja", "th", "id", "fil" })
            Assert.Equal("Photo Studio", Load(code).GetProperty("ps.rs.preset.ours").GetString());
    }

    [Fact]
    public void The_address_is_shown_without_its_scheme()
    {
        Assert.Equal("github.com/ipsusu/IpsuShade", Plugin.DisplayUrl("https://github.com/ipsusu/IpsuShade"));
        Assert.Equal("example.org/x", Plugin.DisplayUrl("example.org/x"));
    }

    private static JsonElement Load(string code) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, code + ".json"))).RootElement;
}
