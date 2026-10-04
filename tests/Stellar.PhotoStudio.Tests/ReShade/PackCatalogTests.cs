using System.Linq;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// Spec 2026-10-03 reshade § 6 / R6: the catalog pins each pack's GitHub archive by commit + sha256 + size, measured
// 2026-10-04 (two downloads, identical bytes). A changed value here must come from a fresh measurement.
// FXShaders / AcerolaFX / OtisFX: owner decision 2026-10-04, default-branch HEAD measured the same way that day
// (report .superpowers/sdd/ps-presets/new-packs-report.md).
public sealed class PackCatalogTests
{
    [Theory]
    [InlineData("standard", "crosire/reshade-shaders", "fd0022170615ce0d8162d219bff07232fa6dd84f",
        "a3b110ba5118f3b944d74f0b0746c21280071d389ed98d615bf3c4b3a1778586", 58091L)]
    [InlineData("sweetfx", "CeeJayDK/SweetFX", "93ddf39b357f5da534ed6d34ba4ec8cc7dcfa361",
        "e1e1d6515d29c65fcf115c9692a1c5f91ffb8d9734e6871bcf67ac48588dbbce", 123972L)]
    [InlineData("prod80", "prod80/prod80-ReShade-Repository", "1c2ed5b093b03c558bfa6aea45c2087052e99554",
        "15b251a3f99901dda81072c3cb8ffa1eb2144dee5399d459a5dde7a50bbd6132", 19803870L)]
    [InlineData("fxshaders", "luluco250/FXShaders", "76365e35c48e30170985ca371e67d8daf8eb9a98",
        "f8abdb10c4239ab0a3f90863da05229f9d0939b717e450c813fab81e6983a4fa", 5572776L)]
    [InlineData("acerolafx", "GarrettGunnell/AcerolaFX", "c33f779b093fa1e25faf0c77ef22c3fe6902e2fe",
        "d7d14c6d3922e4bfe51b927a67810653959f43fe88ced76af7b26dd2c7e336a3", 20846919L)]
    [InlineData("otisfx", "FransBouma/OtisFX", "193aa0bf07ee82fbd5f142b5b813b19c5d169745",
        "96f9b21e0da6820f85ada6be8e5408bc8bfb5b64bb47d2657d95ce24a1ffd77e", 7681128L)]
    public void Catalog_pins_the_measured_archives(string id, string repo, string commit, string sha, long size)
    {
        var p = PackCatalog.Find(id)!;
        Assert.Equal(repo, p.Repo);
        Assert.Equal(commit, p.Commit);
        Assert.Equal(sha, p.Sha256);
        Assert.Equal(size, p.Size);
        Assert.Equal("https://github.com/" + repo + "/archive/" + commit + ".zip", p.ArchiveUrl.ToString());
    }

    [Fact]
    public void Catalog_is_the_six_licensed_packs_and_never_quint()
    {
        // qUINT is "All rights reserved" with no licence grant — dropped (plan § Pack catalog). Do not re-add it.
        Assert.Equal(new[] { "standard", "sweetfx", "prod80", "fxshaders", "acerolafx", "otisfx" },
            PackCatalog.All.Select(p => p.Id).ToArray());
        Assert.Null(PackCatalog.Find("quint"));
    }

    [Fact]
    public void Requires_name_packs_listed_earlier()
    {
        for (var i = 0; i < PackCatalog.All.Count; i++)
            foreach (var r in PackCatalog.All[i].Requires)
                Assert.Contains(r, PackCatalog.All.Take(i).Select(p => p.Id));
        Assert.Equal(new[] { "standard" }, PackCatalog.SweetFx.Requires);
        Assert.Equal(new[] { "standard" }, PackCatalog.Prod80.Requires);
        // FXShaders (27 effects) and OtisFX (11) #include ReShade.fxh / ReShadeUI.fxh / DrawText.fxh and ship none of them.
        Assert.Equal(new[] { "standard" }, PackCatalog.FxShaders.Requires);
        Assert.Equal(new[] { "standard" }, PackCatalog.OtisFx.Requires);
        // AcerolaFX is self-contained: Includes/AcerolaFX_Common.fxh carries its own ReShade namespace; no effect
        // includes ReShade.fxh (all 34 compile with ReShade 6.8.0's front end without standard's headers).
        Assert.Empty(PackCatalog.AcerolaFx.Requires);
    }

    [Fact]
    public void Request_extracts_only_shaders_and_textures_under_the_archive_root()
    {
        var r = PackCatalog.Request(PackCatalog.SweetFx);
        Assert.True(r.ExtractZip);
        Assert.Equal("reshade/packs/sweetfx", r.TargetPath);
        Assert.Equal(PackCatalog.SweetFx.Size, r.MaxBytes);
        Assert.Equal(PackCatalog.SweetFx.Sha256, r.Sha256);
        Assert.Equal(new[]
        {
            "SweetFX-93ddf39b357f5da534ed6d34ba4ec8cc7dcfa361/Shaders/",
            "SweetFX-93ddf39b357f5da534ed6d34ba4ec8cc7dcfa361/Textures/",
        }, r.IncludePrefixes);
    }

    [Fact]
    public void FxShaders_and_OtisFx_extract_their_Shaders_and_Textures_folders()
    {
        Assert.Equal(new[]
        {
            "FXShaders-76365e35c48e30170985ca371e67d8daf8eb9a98/Shaders/",
            "FXShaders-76365e35c48e30170985ca371e67d8daf8eb9a98/Textures/",
        }, PackCatalog.Request(PackCatalog.FxShaders).IncludePrefixes);
        Assert.Equal(new[]
        {
            "OtisFX-193aa0bf07ee82fbd5f142b5b813b19c5d169745/Shaders/",
            "OtisFX-193aa0bf07ee82fbd5f142b5b813b19c5d169745/Textures/",
        }, PackCatalog.Request(PackCatalog.OtisFx).IncludePrefixes);
    }

    // AcerolaFX ships a cut-down Shaders/ReShade.fxh that none of its own effects include. ReShade 6.8.0 resolves an
    // #include from a std::set of every search-path folder (runtime.cpp l. 1554-1574), i.e. in PATH order, and
    // ".../packs/acerolafx/..." sorts before ".../packs/standard/...": installed, that copy would shadow standard's
    // ReShade.fxh for every other pack (measured: FXShaders AspectRatioSuite.fx then fails — its AspectRatio uniform
    // collides with that header's AspectRatio macro). So AcerolaFX extracts its effects by name, never that header.
    [Fact]
    public void AcerolaFx_extracts_its_effects_includes_and_textures_but_never_its_ReShade_fxh()
    {
        const string root = "AcerolaFX-c33f779b093fa1e25faf0c77ef22c3fe6902e2fe/";
        var prefixes = PackCatalog.Request(PackCatalog.AcerolaFx).IncludePrefixes!;
        var effects = new[]
        {
            "Alpha", "ASCII", "AutoExposure", "Blend", "Bloom", "Blur", "BokehBlur", "ChromaKey", "ChromaticAberration",
            "ColorBlindness", "ColorCorrection", "ColorSpaces", "Composition", "CRT", "DifferenceOfGaussians", "Dither",
            "Downscaler", "EdgeDetect", "End", "FilmGrain", "Fog", "Framing", "FXAA", "Gamma", "Halftone", "KuwaharaFilter",
            "PaletteSwap", "PixelSort", "Sharpness", "Start", "Tonemapping", "Vignette", "XeGTAO", "Zoom",
        };
        Assert.Equal(effects.Select(e => root + "Shaders/AcerolaFX_" + e + ".fx")
            .Concat(new[] { root + "Shaders/Includes/", root + "Textures/" }).ToArray(), prefixes);
        Assert.DoesNotContain(prefixes, x => x.EndsWith("ReShade.fxh") || x == root + "Shaders/");
        Assert.Equal("/d/reshade/packs/acerolafx/" + root + "Shaders", PackCatalog.EffectsFolder("/d", PackCatalog.AcerolaFx));
    }

    [Fact]
    public void Folders_keep_the_archive_root_because_the_prefix_stays_in_the_output_path()
    {
        Assert.Equal("/d/reshade/packs/standard/reshade-shaders-fd0022170615ce0d8162d219bff07232fa6dd84f/Shaders",
            PackCatalog.EffectsFolder("/d", PackCatalog.Standard));
        Assert.Equal("/d/reshade/packs/prod80/prod80-ReShade-Repository-1c2ed5b093b03c558bfa6aea45c2087052e99554/Textures",
            PackCatalog.TexturesFolder("/d", PackCatalog.Prod80));
        Assert.Equal("/d/reshade/packs/sweetfx", PackCatalog.PackFolder("/d", PackCatalog.SweetFx));
    }

    [Fact]
    public void Every_pack_shows_a_licence_and_a_https_source()
    {
        foreach (var p in PackCatalog.All)
        {
            Assert.False(string.IsNullOrEmpty(p.License));
            Assert.StartsWith("https://github.com/", p.LicenseUrl);
            Assert.StartsWith("https://github.com/" + p.Repo + "/tree/" + p.Commit, p.SourceUrl);
        }
        Assert.Equal(PackCatalog.MixedLicense, PackCatalog.Standard.License);
        Assert.Equal("MIT", PackCatalog.SweetFx.License);
        Assert.Equal("MIT", PackCatalog.Prod80.License);
        Assert.Equal("MIT", PackCatalog.FxShaders.License);
        Assert.Equal("MIT", PackCatalog.AcerolaFx.License);
        Assert.Equal("MIT", PackCatalog.OtisFx.License);   // MIT for every shader, PandaFX (© Jukka Korhonen) included
        Assert.Equal("https://github.com/FransBouma/OtisFX/blob/193aa0bf07ee82fbd5f142b5b813b19c5d169745/LICENSE",
            PackCatalog.OtisFx.LicenseUrl);
        Assert.Equal("https://github.com/GarrettGunnell/AcerolaFX/blob/c33f779b093fa1e25faf0c77ef22c3fe6902e2fe/LICENSE.md",
            PackCatalog.AcerolaFx.LicenseUrl);
    }
}
