using System;
using System.Collections.Generic;
using System.IO;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>One shader pack fetched from its own GitHub repository at a pinned commit (spec 2026-10-03 reshade § 6, D3 —
/// never re-hosted). <see cref="Sha256"/> and <see cref="Size"/> are GitHub's archive zip for <see cref="Commit"/>, measured
/// 2026-10-04. Init-only properties (not a positional record) keep the constructor under the 5-parameter rule.</summary>
internal sealed record ShaderPack
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>"owner/repo" on GitHub.</summary>
    public string Repo { get; init; } = "";
    public string Commit { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
    /// <summary>SPDX id, or <see cref="PackCatalog.MixedLicense"/> when the pack has no single licence.</summary>
    public string License { get; init; } = "";
    public string LicenseUrl { get; init; } = "";
    /// <summary>Ids of packs whose headers this pack includes (ReShade.fxh / ReShadeUI.fxh); downloaded first.</summary>
    public IReadOnlyList<string> Requires { get; init; } = Array.Empty<string>();
    /// <summary>What the download extracts, relative to <see cref="ArchiveRoot"/>: a folder (trailing '/') or one exact
    /// file. Effects must stay under <c>Shaders/</c> and textures under <c>Textures/</c> (the search paths).</summary>
    public IReadOnlyList<string> Extract { get; init; } = PackCatalog.ShadersAndTextures;
    /// <summary>Lang key of a pack-specific line appended to the pack's "?" help; null for none.</summary>
    public string? HelpNoteKey { get; init; }

    /// <summary>GitHub's archive root folder: "&lt;repo name&gt;-&lt;full commit&gt;".</summary>
    public string ArchiveRoot => Repo.Substring(Repo.IndexOf('/') + 1) + "-" + Commit;
    public Uri ArchiveUrl => new("https://github.com/" + Repo + "/archive/" + Commit + ".zip");
    public string SourceUrl => "https://github.com/" + Repo + "/tree/" + Commit;
    /// <summary>Data-folder-relative zip target. A zip download REPLACES this whole folder (IPluginDownloads contract).</summary>
    public string TargetPath => "reshade/packs/" + Id;
}

/// <summary>The packs Photo Studio offers (plan 2026-10-04 § Pack catalog — licences verified; qUINT dropped: "All rights
/// reserved", no licence grant).</summary>
internal static class PackCatalog
{
    public const string MixedLicense = "mixed";

    /// <summary>The default <see cref="ShaderPack.Extract"/>: the repository's whole Shaders and Textures folders.</summary>
    public static readonly IReadOnlyList<string> ShadersAndTextures = new[] { "Shaders/", "Textures/" };

    // Declared before All: static initializers run in textual order.
    public static readonly ShaderPack Standard = new()
    {
        Id = "standard", Name = "ReShade standard", Repo = "crosire/reshade-shaders",
        Commit = "fd0022170615ce0d8162d219bff07232fa6dd84f",
        Sha256 = "a3b110ba5118f3b944d74f0b0746c21280071d389ed98d615bf3c4b3a1778586", Size = 58091,
        License = MixedLicense,
        LicenseUrl = "https://github.com/crosire/reshade-shaders/tree/fd0022170615ce0d8162d219bff07232fa6dd84f/Shaders",
    };

    public static readonly ShaderPack SweetFx = new()
    {
        Id = "sweetfx", Name = "SweetFX", Repo = "CeeJayDK/SweetFX",
        Commit = "93ddf39b357f5da534ed6d34ba4ec8cc7dcfa361",
        Sha256 = "e1e1d6515d29c65fcf115c9692a1c5f91ffb8d9734e6871bcf67ac48588dbbce", Size = 123972,
        License = "MIT",
        LicenseUrl = "https://github.com/CeeJayDK/SweetFX/blob/93ddf39b357f5da534ed6d34ba4ec8cc7dcfa361/LICENSE",
        Requires = new[] { "standard" },
    };

    public static readonly ShaderPack Prod80 = new()
    {
        Id = "prod80", Name = "prod80", Repo = "prod80/prod80-ReShade-Repository",
        Commit = "1c2ed5b093b03c558bfa6aea45c2087052e99554",
        Sha256 = "15b251a3f99901dda81072c3cb8ffa1eb2144dee5399d459a5dde7a50bbd6132", Size = 19803870,
        License = "MIT",
        LicenseUrl = "https://github.com/prod80/prod80-ReShade-Repository/blob/1c2ed5b093b03c558bfa6aea45c2087052e99554/LICENSE",
        Requires = new[] { "standard" },
    };

    // Owner decision 2026-10-04: FXShaders, AcerolaFX, OtisFX (MIT). Measured that day — default-branch HEAD, two
    // downloads each, identical bytes (.superpowers/sdd/ps-presets/new-packs-report.md).
    public static readonly ShaderPack FxShaders = new()
    {
        Id = "fxshaders", Name = "FXShaders", Repo = "luluco250/FXShaders",
        Commit = "76365e35c48e30170985ca371e67d8daf8eb9a98",
        Sha256 = "f8abdb10c4239ab0a3f90863da05229f9d0939b717e450c813fab81e6983a4fa", Size = 5572776,
        License = "MIT",
        LicenseUrl = "https://github.com/luluco250/FXShaders/blob/76365e35c48e30170985ca371e67d8daf8eb9a98/LICENSE",
        Requires = new[] { "standard" },
    };

    /// <summary>Self-contained (its Common.fxh carries its own ReShade namespace), so no Requires. Extracted effect by
    /// effect: its bundled Shaders/ReShade.fxh would shadow standard's for every other pack, because ReShade resolves an
    /// #include across search-path folders in path order and "acerolafx" sorts before "standard".</summary>
    public static readonly ShaderPack AcerolaFx = new()
    {
        Id = "acerolafx", Name = "AcerolaFX", Repo = "GarrettGunnell/AcerolaFX",
        Commit = "c33f779b093fa1e25faf0c77ef22c3fe6902e2fe",
        Sha256 = "d7d14c6d3922e4bfe51b927a67810653959f43fe88ced76af7b26dd2c7e336a3", Size = 20846919,
        License = "MIT",
        LicenseUrl = "https://github.com/GarrettGunnell/AcerolaFX/blob/c33f779b093fa1e25faf0c77ef22c3fe6902e2fe/LICENSE.md",
        Extract = AcerolaFxExtract(),
        HelpNoteKey = "ps.help.rs.pack.acerolafx",   // effects work only between AcerolaFXStart and AcerolaFXEnd
    };

    /// <summary>MIT for every shader, PandaFX (© Jukka Korhonen) included (its LICENSE names both authors).</summary>
    public static readonly ShaderPack OtisFx = new()
    {
        Id = "otisfx", Name = "OtisFX", Repo = "FransBouma/OtisFX",
        Commit = "193aa0bf07ee82fbd5f142b5b813b19c5d169745",
        Sha256 = "96f9b21e0da6820f85ada6be8e5408bc8bfb5b64bb47d2657d95ce24a1ffd77e", Size = 7681128,
        License = "MIT",
        LicenseUrl = "https://github.com/FransBouma/OtisFX/blob/193aa0bf07ee82fbd5f142b5b813b19c5d169745/LICENSE",
        Requires = new[] { "standard" },
    };

    public static IReadOnlyList<ShaderPack> All { get; } = new[] { Standard, SweetFx, Prod80, FxShaders, AcerolaFx, OtisFx };

    public static ShaderPack? Find(string id)
    {
        foreach (var p in All)
            if (p.Id == id) return p;
        return null;
    }

    /// <summary>The checked download: the exact archive (sha256 + size cap), extracted with only Shaders/ and Textures/
    /// kept. The prefix (archive root included) stays in the output path — see <see cref="EffectsFolder"/>.</summary>
    public static DownloadRequest Request(ShaderPack p)
    {
        var prefixes = new string[p.Extract.Count];
        for (var i = 0; i < prefixes.Length; i++) prefixes[i] = p.ArchiveRoot + "/" + p.Extract[i];
        return new DownloadRequest(p.ArchiveUrl, p.Sha256, p.Size, p.TargetPath, ExtractZip: true, IncludePrefixes: prefixes);
    }

    /// <summary>Every AcerolaFX effect at the pinned commit (34), its Includes folder and its Textures — not Shaders/ReShade.fxh.</summary>
    private static string[] AcerolaFxExtract()
    {
        var effects = new[]
        {
            "Alpha", "ASCII", "AutoExposure", "Blend", "Bloom", "Blur", "BokehBlur", "ChromaKey", "ChromaticAberration",
            "ColorBlindness", "ColorCorrection", "ColorSpaces", "Composition", "CRT", "DifferenceOfGaussians", "Dither",
            "Downscaler", "EdgeDetect", "End", "FilmGrain", "Fog", "Framing", "FXAA", "Gamma", "Halftone", "KuwaharaFilter",
            "PaletteSwap", "PixelSort", "Sharpness", "Start", "Tonemapping", "Vignette", "XeGTAO", "Zoom",
        };
        var list = new string[effects.Length + 2];
        for (var i = 0; i < effects.Length; i++) list[i] = "Shaders/AcerolaFX_" + effects[i] + ".fx";
        list[effects.Length] = "Shaders/Includes/";
        list[effects.Length + 1] = "Textures/";
        return list;
    }

    public static string PackFolder(string dataFolder, ShaderPack p) => Path.Combine(dataFolder, "reshade", "packs", p.Id);

    public static string EffectsFolder(string dataFolder, ShaderPack p) => Path.Combine(PackFolder(dataFolder, p), p.ArchiveRoot, "Shaders");

    public static string TexturesFolder(string dataFolder, ShaderPack p) => Path.Combine(PackFolder(dataFolder, p), p.ArchiveRoot, "Textures");
}
