using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Spec § 8.6: every string localized (en/ja/th/id/fil). Pins the free-camera keys and their format placeholders.
public sealed class FreeCamLangTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "../../../../../Lang");

    private static readonly string[] Keys =
    {
        "hotkey.freecam", "ps.tab.camera", "fc.title.hud", "fc.enter", "fc.exit", "fc.status.off", "fc.status.orbit",
        "fc.status.fly", "fc.you", "fc.mode.orbit", "fc.mode.fly", "fc.hud.line", "fc.badge.frozen", "fc.badge.combat",
        "fc.hint.orbit", "fc.hint.fly", "fc.group.movement", "fc.group.pose", "fc.speed", "fc.sensitivity", "fc.smoothing",
        "fc.leash", "fc.invertY", "fc.entryHide.hud", "fc.entryHide.names", "fc.entryHide.others", "fc.lookAt", "fc.search", "fc.pose.empty", "fc.pose.none",
        "fc.help.movement", "fc.help.speed", "fc.help.sensitivity", "fc.help.smoothing", "fc.help.leash", "fc.help.pose",
        "fc.help.lookAt", "fc.help.entryHides", "fc.help.invertY", "fc.toast.busy", "fc.toast.unavailable", "fc.toast.released", "fc.toast.error",
        "fc.toast.emoteFailed", "fc.toast.hideallMoved", "fc.toast.hideallCleared",
        "fc.reason.scene", "fc.reason.cutscene", "fc.reason.photo", "fc.reason.disconnect",
        "fc.unit.speed", "fc.unit.metres",
    };

    [Theory]
    [InlineData("en")] [InlineData("ja")] [InlineData("th")] [InlineData("id")] [InlineData("fil")]
    public void Every_free_camera_key_exists_with_the_english_placeholders(string code)
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

    // Review P7 (rules § 3): Task 14 fixed a mangled version of the two hide-all migration toasts. Pin the exact
    // English text, and that every locale keeps real quotation marks and never an ASCII "->".
    [Fact]
    public void The_hide_all_toasts_keep_their_exact_english_text()
    {
        var en = Load("en");
        Assert.Equal("Photo Studio: \u201cHide everything\u201d moved to Ctrl+F10. Alt+F10 now turns the free camera on and off.",
            en.GetProperty("fc.toast.hideallMoved").GetString());
        Assert.Equal("Photo Studio: Alt+F10 now turns the free camera on and off. \u201cHide everything\u201d has no key because "
            + "Ctrl+F10 is taken \u2014 set one in Settings \u2192 Hotkeys.", en.GetProperty("fc.toast.hideallCleared").GetString());
    }

    [Theory]
    [InlineData("en")] [InlineData("ja")] [InlineData("th")] [InlineData("id")] [InlineData("fil")]
    public void The_hide_all_toasts_use_real_quotes_and_no_ascii_arrow(string code)
    {
        var cat = Load(code);
        foreach (var key in new[] { "fc.toast.hideallMoved", "fc.toast.hideallCleared" })
        {
            var v = cat.GetProperty(key).GetString()!;
            var curly = v.Contains('\u201c') && v.Contains('\u201d');
            var corner = v.Contains('\u300c') && v.Contains('\u300d');
            Assert.True(curly || corner, $"{code}.{key} lost its quotation marks: {v}");
            Assert.DoesNotContain("->", v);
        }
    }

    private static JsonElement Load(string code) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, code + ".json"))).RootElement;
}
