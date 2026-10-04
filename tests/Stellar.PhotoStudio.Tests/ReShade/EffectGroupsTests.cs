using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

public sealed class EffectGroupsTests
{
    private static FxRow Row(string name, string file) => new(new ReShadeTechnique(name, file, false, false), name, false);

    private static readonly IReadOnlyDictionary<string, string> Index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Vibrance.fx"] = "SweetFX", ["Curves.fx"] = "SweetFX", ["MagicHDR.fx"] = "FXShaders", ["Emphasize.fx"] = "OtisFX",
    };

    private static readonly string[] Order = { "ReShade standard", "SweetFX", "FXShaders", "OtisFX" };

    [Fact]
    public void Effects_sit_under_their_pack_in_pack_order_with_unknown_files_last()
    {
        var rows = new[] { Row("Mine", "Mine.fx"), Row("Vibrance", "Vibrance.fx"), Row("Emphasize", "Emphasize.fx"),
            Row("Curves", "Curves.fx"), Row("MagicHDR", "MagicHDR.fx") };
        var items = EffectGroups.Build(rows, Index, Order, _ => true);
        Assert.Equal(new[] { "SweetFX", "Curves", "Vibrance", "FXShaders", "MagicHDR", "OtisFX", "Emphasize", "", "Mine" },
            items.Select(i => i.IsHeader ? i.Group : i.Row!.Label));
    }

    [Fact]
    public void A_folded_pack_shows_only_its_heading_which_still_holds_its_rows()
    {
        var rows = new[] { Row("Vibrance", "Vibrance.fx"), Row("Curves", "Curves.fx"), Row("MagicHDR", "MagicHDR.fx") };
        var items = EffectGroups.Build(rows, Index, Order, g => g != "SweetFX");
        Assert.Equal(new[] { "SweetFX", "FXShaders", "MagicHDR" }, items.Select(i => i.IsHeader ? i.Group : i.Row!.Label));
        Assert.Equal(2, items[0].Rows!.Count);   // the heading counts its effects even when folded
    }

    [Fact]
    public void Packs_without_effects_get_no_heading()
    {
        var items = EffectGroups.Build(new[] { Row("Vibrance", "Vibrance.fx") }, Index, Order, _ => false);
        Assert.Single(items);
        Assert.Equal("SweetFX", items[0].Group);
    }

    [Fact]
    public void Index_maps_effect_files_found_anywhere_under_a_pack_folder_and_the_first_pack_wins()
    {
        var root = Path.Combine(Path.GetTempPath(), "fxgroups-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", "Sub"));
            Directory.CreateDirectory(Path.Combine(root, "b"));
            File.WriteAllText(Path.Combine(root, "a", "Sub", "Deep.fx"), "");
            File.WriteAllText(Path.Combine(root, "a", "Same.fx"), "");
            File.WriteAllText(Path.Combine(root, "b", "same.fx"), "");
            File.WriteAllText(Path.Combine(root, "b", "Header.fxh"), "");
            var index = EffectGroups.Index(new[] { ("A", Path.Combine(root, "a")), ("B", Path.Combine(root, "b")),
                ("Missing", Path.Combine(root, "nope")) });
            Assert.Equal("A", index["Deep.fx"]);
            Assert.Equal("A", index["SAME.fx"]);
            Assert.False(index.ContainsKey("Header.fxh"));
            Assert.Equal(2, index.Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
