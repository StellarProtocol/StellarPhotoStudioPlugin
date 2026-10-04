using System;
using System.Linq;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// Spec 2026-10-03 reshade § 12 V4; docs/recon/reshade-preset-sources.md. Community presets are pinned to a commit and
// checked by sha256 + exact size, re-measured 2026-10-04 (plan § Re-measure — identical to the research download).
// A changed value here must come from a fresh measurement. Coverage re-measured 2026-10-04 against all six packs
// (technique counts only when file AND technique name match): Stella Medium/High 2/3 -> 3/3 — MagicHDR@MagicHDR.fx is
// FXShaders' (technique "MagicHDR", uniforms match the preset's section); every other preset unchanged.
public sealed class PresetCatalogTests
{
    [Theory]
    [InlineData("starluxe-galactic", "StarLuxe Galactic.ini",
        "https://raw.githubusercontent.com/Dimitri-Matheus/StarLuxe/6b82aff25e9eb3e69c3927ed724a2f463c92c40e/script/Presets/Galactic/Galactic.ini",
        "19db3f3e5ce8a8a75f2f27cf941758d29590aa236049b43a6e6ba44d61f3e2ac", 6375L, "GPL-3.0", 5, 5, false)]
    [InlineData("starluxe-legacy", "StarLuxe Legacy.ini",
        "https://raw.githubusercontent.com/Dimitri-Matheus/StarLuxe/6b82aff25e9eb3e69c3927ed724a2f463c92c40e/script/Presets/Legacy/Legacy.ini",
        "e99e12cc86705f61f410eff750d67204fd50ffee845e60f126e48c9dcda03427", 9443L, "GPL-3.0", 5, 5, false)]
    [InlineData("starluxe-luminescence", "StarLuxe Luminescence.ini",
        "https://raw.githubusercontent.com/Dimitri-Matheus/StarLuxe/6b82aff25e9eb3e69c3927ed724a2f463c92c40e/script/Presets/Luminescence/Luminescence.ini",
        "5e1b23f882fede42f1cf7d9cd0076574daae354710eca19e143cec00e4571d63", 38217L, "GPL-3.0", 5, 5, true)]
    [InlineData("stella-medium", "Stella Medium.ini",
        "https://raw.githubusercontent.com/Genshin-Stella-Mod/resources/a15ae11517dad6c5681beb212422724c4455a276/public/resources/ReShade/Presets/1.%20Default%20preset%20-%20Medium%20settings.ini",
        "320356eab91018c4677be724626a6532fa3801e2cea4f48e7df242a6c6bca93a", 1624L, "CC-BY-SA-4.0", 3, 3, true)]
    [InlineData("stella-high", "Stella High.ini",
        "https://raw.githubusercontent.com/Genshin-Stella-Mod/resources/a15ae11517dad6c5681beb212422724c4455a276/public/resources/ReShade/Presets/2.%20Default%20preset%20-%20High%20settings.ini",
        "61c65ecbc73e00ad7f2a319df66dcc6c1a960098d63c865b8054ede52f9da043", 1624L, "CC-BY-SA-4.0", 3, 3, true)]
    [InlineData("okami-cityruins", "Okami City Ruins.ini",
        "https://raw.githubusercontent.com/MeynanAneytha/YomigamiOkami-reshade-shaders/53e9fe085845093f50189dc5cce9419e88e423ed/reshade-presets/Okami/OkamiNierAutomata_CityRuins.ini",
        "9a0d12003cd004f18a437966f7e278b8d259695bee5783e56b047bfc3fcb5677", 2863L, "MIT", 13, 19, false)]
    public void Community_presets_pin_the_measured_files(string id, string file, string raw, string sha, long size, string license,
        int covered, int total, bool adjusted)
    {
        var e = PresetCatalog.Find(id)!;
        Assert.Equal(PresetKind.Community, e.Kind);
        Assert.Equal(file, e.FileName);
        Assert.Equal(raw, e.RawUrl);
        Assert.Equal(sha, e.Sha256);
        Assert.Equal(size, e.Size);
        Assert.Equal(license, e.License);
        Assert.Equal(covered, e.Covered);
        Assert.Equal(total, e.Total);
        Assert.Equal(adjusted, e.Adjusted);
        Assert.Contains("/" + e.Commit + "/", e.RawUrl);   // pinned at a commit, never a branch
        Assert.Matches("^[0-9a-f]{40}$", e.Commit);
        Assert.StartsWith("https://github.com/", e.LicenseUrl);
        Assert.Contains(e.Commit, e.LicenseUrl);
        Assert.Contains(e.Commit, e.PageUrl);
    }

    [Fact]
    public void Community_authors_and_packs_are_pinned()
    {
        Assert.Equal("Dimitri-Matheus (StarLuxe)", PresetCatalog.Find("starluxe-galactic")!.Author);
        Assert.Equal("Sefinek (Genshin Stella Mod)", PresetCatalog.Find("stella-medium")!.Author);
        Assert.Equal("Yomigami Okami, port by Meynan", PresetCatalog.Find("okami-cityruins")!.Author);
        Assert.Equal(new[] { "sweetfx" }, PresetCatalog.Find("starluxe-legacy")!.Packs);
        Assert.Equal(new[] { "prod80", "fxshaders" }, PresetCatalog.Find("stella-high")!.Packs);
        Assert.Equal(new[] { "prod80", "fxshaders" }, PresetCatalog.Find("stella-medium")!.Packs);
        Assert.False(PresetCatalog.Find("stella-medium")!.Partial);   // no "partial" badge any more
        Assert.True(PresetCatalog.Find("okami-cityruins")!.Partial);   // 13/19 — the new packs carry none of its six
        Assert.Equal(new[] { "standard", "sweetfx", "prod80" }, PresetCatalog.Find("okami-cityruins")!.Packs);
        Assert.Equal(new[] { "fLUT_TextureName" }, PresetCatalog.Find("starluxe-luminescence")!.DropDefinitions);
        Assert.Empty(PresetCatalog.Find("stella-medium")!.DropDefinitions);   // its only override is the RESHADE_DEPTH_* rule
    }

    // Review fix round 2, item 1: OwnLicense must be the plugin's EXACT licence (the README's own closing line says
    // "AGPL-3.0-or-later"), not the shorter SPDX-adjacent "AGPL-3.0".
    [Fact]
    public void Own_licence_is_the_plugins_exact_licence()
    {
        Assert.Equal("AGPL-3.0-or-later", PresetCatalog.OwnLicense);
    }

    [Theory]
    [InlineData("cinematic-warm", "Cinematic warm.ini", "sweetfx,prod80")]
    [InlineData("soft-anime", "Soft anime.ini", "sweetfx,prod80")]
    [InlineData("cool-night", "Cool night.ini", "sweetfx,prod80")]
    [InlineData("clean-sharpen", "Clean sharpen.ini", "sweetfx")]
    public void Own_presets_are_listed_with_their_packs_and_no_download(string id, string file, string packs)
    {
        var e = PresetCatalog.Find(id)!;
        Assert.Equal(PresetKind.Own, e.Kind);
        Assert.Equal(file, e.FileName);
        Assert.Equal(packs.Split(','), e.Packs);
        Assert.Equal(PresetCatalog.OwnLicense, e.License);
        Assert.Equal("", e.RawUrl);
        Assert.Equal("", e.Sha256);
        Assert.Null(PresetCatalog.Request(e));
        Assert.True(e.Installable);
    }

    // NOTE (deviation from the brief, precedent in ReShadeViewTests.cs): LinkReason is `internal`; a `public` xunit
    // Theory method taking it as a parameter is CS0051 even with InternalsVisibleTo (IVT does not reach a third
    // assembly referencing the test assembly). Indirected through nameof()/ToString(), same workaround as ReShadePanel.
    [Theory]
    [InlineData("ipsushade", "https://github.com/ipsusu/IpsuShade", "ipsusu", nameof(LinkReason.AskAuthor))]
    [InlineData("steaxs-filter-pack", "https://github.com/steaxss/STEAXS-FILTER-PACK", "steaxss", nameof(LinkReason.NoLicence))]
    [InlineData("norange-wuwa", "https://gamebanana.com/tools/18631", "No Range", nameof(LinkReason.AskAuthor))]
    [InlineData("endfield-official-filter", "https://gamebanana.com/mods/654035", "Eclyse069", nameof(LinkReason.AskAuthor))]
    [InlineData("vibrantsharpen-zzz", "https://gamebanana.com/mods/607194", "Yamilowo", nameof(LinkReason.AskAuthor))]
    public void Link_only_presets_carry_a_page_and_nothing_downloadable(string id, string page, string author, string reason)
    {
        var e = PresetCatalog.Find(id)!;
        Assert.Equal(PresetKind.LinkOnly, e.Kind);
        Assert.Equal(page, e.PageUrl);
        Assert.Equal(author, e.Author);
        Assert.Equal(reason, e.Reason.ToString());
        Assert.False(e.Installable);
        Assert.Equal("", e.FileName);
        Assert.Equal("", e.RawUrl);
        Assert.Equal("", e.Sha256);
        Assert.Empty(e.Packs);
        Assert.Null(PresetCatalog.Request(e));   // never downloaded
    }

    [Fact]
    public void Order_is_own_then_community_then_link_only_and_ids_and_files_are_unique()
    {
        var kinds = PresetCatalog.All.Select(e => e.Kind).ToArray();
        Assert.Equal(kinds.OrderBy(k => (int)k).ToArray(), kinds);
        Assert.Equal(15, PresetCatalog.All.Count);
        Assert.Equal(PresetCatalog.All.Count, PresetCatalog.All.Select(e => e.Id).Distinct().Count());
        var files = PresetCatalog.All.Where(e => e.Installable).Select(e => e.FileName.ToLowerInvariant()).ToArray();
        Assert.Equal(files.Length, files.Distinct().Count());
        Assert.DoesNotContain(ReShadePresets.DefaultFile.ToLowerInvariant(), files);
        Assert.All(PresetCatalog.All, e => Assert.Matches("^[a-z0-9-]+$", e.Id));   // used in a data-folder path
    }

    [Fact]
    public void Every_pack_id_exists_and_requires_are_added_in_pack_order()
    {
        foreach (var e in PresetCatalog.All)
            foreach (var id in e.Packs)
                Assert.NotNull(PackCatalog.Find(id));
        Assert.Equal(new[] { "standard", "sweetfx" },
            PresetCatalog.PacksWithRequires(PresetCatalog.Find("starluxe-galactic")!).Select(p => p.Id).ToArray());
        Assert.Equal(new[] { "standard", "prod80", "fxshaders" },   // installing Stella also fetches FXShaders (MagicHDR)
            PresetCatalog.PacksWithRequires(PresetCatalog.Find("stella-medium")!).Select(p => p.Id).ToArray());
        Assert.Equal(new[] { "standard", "sweetfx", "prod80" },
            PresetCatalog.PacksWithRequires(PresetCatalog.Find("cinematic-warm")!).Select(p => p.Id).ToArray());
        Assert.Empty(PresetCatalog.PacksWithRequires(PresetCatalog.Find("ipsushade")!));
    }

    [Fact]
    public void A_community_request_is_a_plain_file_into_preset_sources_capped_at_its_size()
    {
        var e = PresetCatalog.Find("stella-medium")!;
        var r = PresetCatalog.Request(e)!;
        Assert.Equal(e.RawUrl, r.Url.AbsoluteUri);   // AbsoluteUri keeps %20 (ToString would unescape it)
        Assert.Equal(e.Sha256, r.Sha256);
        Assert.Equal(e.Size, r.MaxBytes);
        Assert.False(r.ExtractZip);
        Assert.Null(r.IncludePrefixes);
        Assert.Equal("reshade/preset-sources/stella-medium.ini", r.TargetPath);   // a FILE path (IPluginDownloads contract)
        Assert.Equal(r.TargetPath, e.SourcePath);
    }
}
