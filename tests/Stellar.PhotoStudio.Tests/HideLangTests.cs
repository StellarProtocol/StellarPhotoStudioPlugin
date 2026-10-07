using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

// 1.7.0: every new hide toggle, the FOV row and the FOV hotkeys are localized in all five catalogues, with the English
// placeholders ({0} = FOV in key, {1} = FOV out key, {2} = reset key; {0} = free-camera key in the off hint).
public sealed class HideLangTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "../../../../../Lang");

    private static readonly string[] Keys =
    {
        "hotkey.fovin", "hotkey.fovout", "hotkey.fovreset",
        "ps.hide.me", "ps.help.hide.me", "ps.hide.spiritEcho", "ps.help.hide.spiritEcho",
        "ps.hide.adventurers", "ps.help.hide.adventurers", "ps.hide.npcs", "ps.help.hide.npcs",
        "ps.hide.enemies", "ps.help.hide.enemies", "ps.hide.weapons", "ps.help.hide.weapons",
        "ps.hide.friends", "ps.help.hide.friends", "ps.hide.party", "ps.help.hide.party",
        "ps.hide.guild", "ps.help.hide.guild", "ps.hide.collectibles", "ps.help.hide.collectibles",
        "ps.hide.otherSpiritEchoes", "ps.help.hide.otherSpiritEchoes",
        "ps.look.fov", "ps.look.fovAngle", "ps.help.look.fov", "ps.look.fovOff",
    };

    [Theory]
    [InlineData("en")] [InlineData("ja")] [InlineData("th")] [InlineData("id")] [InlineData("fil")]
    public void Every_new_key_exists_with_the_english_placeholders(string code)
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

    [Fact]
    public void English_labels_are_the_game_photo_screens_own_names()
    {
        var en = Load("en");
        Assert.Equal(new[] { "Me", "My own Spirit Echo", "Other adventurers", "Non-players", "Enemy", "Weapon", "Friends",
                "Party", "Guild", "Collectible", "Other Spirit Echo" },
            Array.ConvertAll(new[] { "me", "spiritEcho", "adventurers", "npcs", "enemies", "weapons", "friends", "party",
                "guild", "collectibles", "otherSpiritEchoes" }, k => en.GetProperty("ps.hide." + k).GetString()));
    }

    // Framework 2.20.0 per-character hides (owner go 2026-10-07): Weapon hides EVERY player's weapon, and Friends / Party /
    // Guild are real hides that work while Other adventurers is shown. The English help must say so — the 1.7.0 first
    // draft described the game's own narrower switches.
    [Fact]
    public void English_help_describes_the_per_character_hides()
    {
        var en = Load("en");
        Assert.Contains("every player's weapon", en.GetProperty("ps.help.hide.weapons").GetString());
        foreach (var k in new[] { "friends", "party", "guild" })
            Assert.Contains("even while Other adventurers is shown", en.GetProperty("ps.help.hide." + k).GetString());
    }

    private static JsonElement Load(string code) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, code + ".json"))).RootElement;
}
