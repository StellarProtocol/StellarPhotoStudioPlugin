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

    public static IReadOnlyList<ShaderPack> All { get; } = new[] { Standard, SweetFx, Prod80 };

    public static ShaderPack? Find(string id)
    {
        foreach (var p in All)
            if (p.Id == id) return p;
        return null;
    }

    /// <summary>The checked download: the exact archive (sha256 + size cap), extracted with only Shaders/ and Textures/
    /// kept. The prefix (archive root included) stays in the output path — see <see cref="EffectsFolder"/>.</summary>
    public static DownloadRequest Request(ShaderPack p) => new(p.ArchiveUrl, p.Sha256, p.Size, p.TargetPath, ExtractZip: true,
        IncludePrefixes: new[] { p.ArchiveRoot + "/Shaders/", p.ArchiveRoot + "/Textures/" });

    public static string PackFolder(string dataFolder, ShaderPack p) => Path.Combine(dataFolder, "reshade", "packs", p.Id);

    public static string EffectsFolder(string dataFolder, ShaderPack p) => Path.Combine(PackFolder(dataFolder, p), p.ArchiveRoot, "Shaders");

    public static string TexturesFolder(string dataFolder, ShaderPack p) => Path.Combine(PackFolder(dataFolder, p), p.ArchiveRoot, "Textures");
}
