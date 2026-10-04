using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// Owner 2026-10-05: "the preset should have None option".
public sealed class ReShadePresetsNoneTests
{
    private const string Folder = "/data/reshade/presets";

    [Fact]
    public void None_is_the_first_choice_and_points_at_the_empty_preset()
    {
        var o = ReShadePresets.Options(Folder, new[] { "Cinematic warm.ini" }, null, n => n, "None");
        Assert.Equal(new[] { "None", "Cinematic warm" }, o.Labels);
        Assert.Equal(ReShadePaths.PathFor(Folder, ReShadePresets.NoneFile), o.Paths[0]);
    }

    [Fact]
    public void None_is_selected_when_it_is_the_current_preset()
    {
        var o = ReShadePresets.Options(Folder, new[] { "Cinematic warm.ini" }, ReShadePaths.PathFor(Folder, ReShadePresets.NoneFile), n => n, "None");
        Assert.Equal(0, o.Selected);
    }

    [Fact]
    public void The_empty_preset_file_is_never_listed_as_a_normal_preset()
    {
        var list = ReShadePresets.List(new[] { Folder + "/_none.ini", Folder + "/Noir.ini", Folder + "/_NONE.INI" });
        Assert.Equal(new[] { "Noir.ini" }, list);
    }

    [Fact]
    public void The_empty_preset_turns_every_effect_off()
    {
        Assert.Contains("Techniques=\r\n", ReShadePresets.NoneContent);
        Assert.DoesNotContain("@", ReShadePresets.NoneContent);
    }
}
