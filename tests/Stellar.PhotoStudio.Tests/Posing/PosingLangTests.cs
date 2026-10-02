using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Stellar.PhotoStudio.Tests.Posing;

// Spec 2026-10-02 § 3: every Person-group string exists in en/ja/th/id/fil with the English placeholders, and the
// spec's verbatim English lines stay verbatim.
public sealed class PosingLangTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "../../../../../Lang");

    private static readonly string[] Keys =
    {
        "pz.group.person", "pz.kind.you", "pz.kind.player", "pz.kind.npc", "pz.loading", "pz.hint.select", "pz.note.copy",
        "pz.failed", "pz.off", "pz.pose.pick", "pz.moment", "pz.sub.expression", "pz.expression.none", "pz.hold", "pz.sub.head",
        "pz.sub.eyes", "pz.look.default", "pz.look.lens", "pz.look.free", "pz.lock", "pz.rotate", "pz.reset", "pz.hint.cloth",
        "pz.unit.percent", "pz.unit.degrees", "pz.pose.current", "pz.help.person", "pz.help.moment", "pz.help.expression", "pz.help.head",
        "pz.help.eyes", "pz.help.rotate", "pz.full", "fc.help.pose",
        // Scene group + SCENE pill (scene-stays spec 2026-10-02).
        "sc.group", "sc.help", "sc.freeze", "sc.unfreeze", "sc.reset", "sc.status.empty", "sc.status.frozen",
        "sc.status.posed", "sc.pill.title", "sc.pill.posed", "sc.pill.back", "fc.status.kept", "pz.hint.selectOff",
    };

    [Theory]
    [InlineData("en")] [InlineData("ja")] [InlineData("th")] [InlineData("id")] [InlineData("fil")]
    public void Every_person_key_exists_with_the_english_placeholders(string code)
    {
        var en = Load("en");
        var cat = Load(code);
        foreach (var key in Keys)
        {
            Assert.True(cat.TryGetProperty(key, out var v), $"{code} lacks {key}");
            Assert.False(string.IsNullOrWhiteSpace(v.GetString()), $"{code}.{key} is empty");
            Assert.Equal(en.GetProperty(key).GetString()!.Contains("{0}"), v.GetString()!.Contains("{0}"));
        }
    }

    [Fact]
    public void The_spec_lines_stay_verbatim_in_english()
    {
        var en = Load("en");
        Assert.Equal("Click a character to select · Backspace for you", en.GetProperty("pz.hint.select").GetString());
        // Scene-stays spec (2026-10-02, owner-approved mockup): copies outlive the free camera, so the note names the reset.
        Assert.Equal("Posing a local copy — only you see it. The real player is hidden until you reset the scene.", en.GetProperty("pz.note.copy").GetString());
        Assert.Equal("Reset scene", en.GetProperty("sc.reset").GetString());
        Assert.Equal("Press Space to freeze cloth too", en.GetProperty("pz.hint.cloth").GetString());
        Assert.Equal("Reset this person", en.GetProperty("pz.reset").GetString());
    }

    [Theory]
    [InlineData("en")] [InlineData("ja")] [InlineData("th")] [InlineData("id")] [InlineData("fil")]
    public void The_pose_help_no_longer_mentions_the_emote_wheel(string code)
    {
        var v = Load(code).GetProperty("fc.help.pose").GetString()!;
        Assert.Contains("❚❚", v);   // ❚❚ — the pause button it explains
    }

    private static JsonElement Load(string code) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, code + ".json"))).RootElement;
}
