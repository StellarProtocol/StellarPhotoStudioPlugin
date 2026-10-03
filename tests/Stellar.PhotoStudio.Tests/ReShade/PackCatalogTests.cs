using System.Linq;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// Spec 2026-10-03 reshade § 6 / R6: the catalog pins each pack's GitHub archive by commit + sha256 + size, measured
// 2026-10-04 (two downloads, identical bytes). A changed value here must come from a fresh measurement.
public sealed class PackCatalogTests
{
    [Theory]
    [InlineData("standard", "crosire/reshade-shaders", "fd0022170615ce0d8162d219bff07232fa6dd84f",
        "a3b110ba5118f3b944d74f0b0746c21280071d389ed98d615bf3c4b3a1778586", 58091L)]
    [InlineData("sweetfx", "CeeJayDK/SweetFX", "93ddf39b357f5da534ed6d34ba4ec8cc7dcfa361",
        "e1e1d6515d29c65fcf115c9692a1c5f91ffb8d9734e6871bcf67ac48588dbbce", 123972L)]
    [InlineData("prod80", "prod80/prod80-ReShade-Repository", "1c2ed5b093b03c558bfa6aea45c2087052e99554",
        "15b251a3f99901dda81072c3cb8ffa1eb2144dee5399d459a5dde7a50bbd6132", 19803870L)]
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
    public void Catalog_is_standard_sweetfx_prod80_and_never_quint()
    {
        // qUINT is "All rights reserved" with no licence grant — dropped (plan § Pack catalog). Do not re-add it.
        Assert.Equal(new[] { "standard", "sweetfx", "prod80" }, PackCatalog.All.Select(p => p.Id).ToArray());
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
    }
}
