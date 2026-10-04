using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio.ReShade;

/// <summary>Where a ReShade preset comes from (spec 2026-10-03 reshade § 12 V4).</summary>
internal enum PresetKind { Own, Community, LinkOnly }

/// <summary>Why a link-only preset is not downloaded for the player.</summary>
internal enum LinkReason { None, NoLicence, AskAuthor }

/// <summary>One entry of the Presets list. Init-only properties keep the constructor under the 5-parameter rule.
/// Community entries are pinned like shader packs: raw file at a commit + sha256 + exact size (plan § The catalog).</summary>
internal sealed record PresetEntry
{
    public string Id { get; init; } = "";
    /// <summary>Proper name shown in the list (not localized: the Preset dropdown shows the file name anyway).</summary>
    public string Name { get; init; } = "";
    public PresetKind Kind { get; init; }
    /// <summary>File in Photo Studio's presets folder; "" for link-only. Stable forever: Look presets store it.</summary>
    public string FileName { get; init; } = "";
    public string Author { get; init; } = "";
    public string License { get; init; } = "";
    public string LicenseUrl { get; init; } = "";
    /// <summary>The author's page (link-only) or the source folder at the pinned commit (community).</summary>
    public string PageUrl { get; init; } = "";
    /// <summary>Community only: the raw file at <see cref="Commit"/>.</summary>
    public string RawUrl { get; init; } = "";
    public string Commit { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
    /// <summary>Pack ids whose techniques the preset enables (requires are added by <see cref="PresetCatalog.PacksWithRequires"/>).</summary>
    public IReadOnlyList<string> Packs { get; init; } = Array.Empty<string>();
    /// <summary>Enabled techniques found in the three pinned packs / all enabled techniques (community only).</summary>
    public int Covered { get; init; }
    public int Total { get; init; }
    /// <summary>The installed copy leaves out settings made for another game (measured: plan D-OVR table).</summary>
    public bool Adjusted { get; init; }
    /// <summary>Definitions left out of the installed copy besides every RESHADE_DEPTH_* one (textures no pack ships).</summary>
    public IReadOnlyList<string> DropDefinitions { get; init; } = Array.Empty<string>();
    public LinkReason Reason { get; init; }

    public bool Installable => Kind != PresetKind.LinkOnly;
    public bool Partial => Covered < Total;
    /// <summary>Data-folder-relative FILE path of the sha256-checked download (never written again after the download).</summary>
    public string SourcePath => "reshade/preset-sources/" + Id + ".ini";
}

/// <summary>The presets Photo Studio offers (plan 2026-10-04 presets § The catalog; licences verified at each commit).</summary>
internal static class PresetCatalog
{
    /// <summary>Licence shown for Photo Studio's own presets: the plugin repository's (plan gap G2 — the owner may grant MIT).</summary>
    public const string OwnLicense = "AGPL-3.0";
    public const string OwnLicenseUrl = "https://github.com/StellarProtocol/StellarPhotoStudioPlugin/blob/main/LICENSE";
    private const string OwnPage = "https://github.com/StellarProtocol/StellarPhotoStudioPlugin/tree/main/Resources/Presets";

    private const string StarLuxeCommit = "6b82aff25e9eb3e69c3927ed724a2f463c92c40e";
    private const string StarLuxeRepo = "https://github.com/Dimitri-Matheus/StarLuxe";
    private const string StarLuxeRaw = "https://raw.githubusercontent.com/Dimitri-Matheus/StarLuxe/" + StarLuxeCommit + "/script/Presets/";
    private const string StarLuxeAuthor = "Dimitri-Matheus (StarLuxe)";

    private const string StellaCommit = "a15ae11517dad6c5681beb212422724c4455a276";
    private const string StellaRepo = "https://github.com/Genshin-Stella-Mod/resources";
    private const string StellaRaw = "https://raw.githubusercontent.com/Genshin-Stella-Mod/resources/" + StellaCommit + "/public/resources/ReShade/Presets/";
    private const string StellaAuthor = "Sefinek (Genshin Stella Mod)";

    private const string OkamiCommit = "53e9fe085845093f50189dc5cce9419e88e423ed";
    private const string OkamiRepo = "https://github.com/MeynanAneytha/YomigamiOkami-reshade-shaders";

    // Declared before All: static initializers run in textual order.
    public static readonly PresetEntry CinematicWarm = Own("cinematic-warm", "Cinematic warm", "sweetfx", "prod80");
    public static readonly PresetEntry SoftAnime = Own("soft-anime", "Soft anime", "sweetfx", "prod80");
    public static readonly PresetEntry CoolNight = Own("cool-night", "Cool night", "sweetfx", "prod80");
    public static readonly PresetEntry CleanSharpen = Own("clean-sharpen", "Clean sharpen", "sweetfx");

    public static readonly PresetEntry StarLuxeGalactic = StarLuxe("starluxe-galactic", "Galactic",
        "19db3f3e5ce8a8a75f2f27cf941758d29590aa236049b43a6e6ba44d61f3e2ac", 6375) with { Covered = 5, Total = 5 };
    public static readonly PresetEntry StarLuxeLegacy = StarLuxe("starluxe-legacy", "Legacy",
        "e99e12cc86705f61f410eff750d67204fd50ffee845e60f126e48c9dcda03427", 9443) with { Covered = 5, Total = 5 };
    public static readonly PresetEntry StarLuxeLuminescence = StarLuxe("starluxe-luminescence", "Luminescence",
        "5e1b23f882fede42f1cf7d9cd0076574daae354710eca19e143cec00e4571d63", 38217) with
    {
        Covered = 5, Total = 5, Adjusted = true, DropDefinitions = new[] { "fLUT_TextureName" },   // "DarkNRich.png": no pack ships it
    };

    public static readonly PresetEntry StellaMedium = Stella("stella-medium", "Stella Medium",
        "1.%20Default%20preset%20-%20Medium%20settings.ini", "320356eab91018c4677be724626a6532fa3801e2cea4f48e7df242a6c6bca93a");
    public static readonly PresetEntry StellaHigh = Stella("stella-high", "Stella High",
        "2.%20Default%20preset%20-%20High%20settings.ini", "61c65ecbc73e00ad7f2a319df66dcc6c1a960098d63c865b8054ede52f9da043");

    public static readonly PresetEntry OkamiCityRuins = new()
    {
        Id = "okami-cityruins", Name = "Okami City Ruins", Kind = PresetKind.Community, FileName = "Okami City Ruins.ini",
        Author = "Yomigami Okami, port by Meynan", License = "MIT", LicenseUrl = OkamiRepo + "/blob/" + OkamiCommit + "/LICENSE",
        PageUrl = OkamiRepo + "/tree/" + OkamiCommit + "/reshade-presets/Okami", Commit = OkamiCommit,
        RawUrl = "https://raw.githubusercontent.com/MeynanAneytha/YomigamiOkami-reshade-shaders/" + OkamiCommit
            + "/reshade-presets/Okami/OkamiNierAutomata_CityRuins.ini",
        Sha256 = "9a0d12003cd004f18a437966f7e278b8d259695bee5783e56b047bfc3fcb5677", Size = 2863,
        Packs = new[] { "standard", "sweetfx", "prod80" }, Covered = 13, Total = 19,
    };

    public static readonly PresetEntry IpsuShade = Link("ipsushade", "IpsuShade", "ipsusu", "https://github.com/ipsusu/IpsuShade", LinkReason.AskAuthor);
    public static readonly PresetEntry Steaxs = Link("steaxs-filter-pack", "STEAXS Filter Pack", "steaxss",
        "https://github.com/steaxss/STEAXS-FILTER-PACK", LinkReason.NoLicence);
    public static readonly PresetEntry NoRange = Link("norange-wuwa", "NoRange (Wuthering Waves)", "No Range",
        "https://gamebanana.com/tools/18631", LinkReason.AskAuthor);
    public static readonly PresetEntry EndfieldOfficial = Link("endfield-official-filter", "Official Filter (Endfield)", "Eclyse069",
        "https://gamebanana.com/mods/654035", LinkReason.AskAuthor);
    public static readonly PresetEntry VibrantSharpen = Link("vibrantsharpen-zzz", "VibrantSharpen (ZZZ)", "Yamilowo",
        "https://gamebanana.com/mods/607194", LinkReason.AskAuthor);

    public static IReadOnlyList<PresetEntry> All { get; } = new[]
    {
        CinematicWarm, SoftAnime, CoolNight, CleanSharpen,
        StarLuxeGalactic, StarLuxeLegacy, StarLuxeLuminescence, StellaMedium, StellaHigh, OkamiCityRuins,
        IpsuShade, Steaxs, NoRange, EndfieldOfficial, VibrantSharpen,
    };

    public static PresetEntry? Find(string id)
    {
        foreach (var e in All)
            if (e.Id == id) return e;
        return null;
    }

    /// <summary>The packs to install before the preset, in <see cref="PackCatalog.All"/> order (a pack's requires included).</summary>
    public static IReadOnlyList<ShaderPack> PacksWithRequires(PresetEntry e)
    {
        var list = new List<ShaderPack>();
        foreach (var p in PackCatalog.All)
        {
            var needed = false;
            foreach (var id in e.Packs)
            {
                needed |= id == p.Id;
                if (PackCatalog.Find(id) is { } q)
                    foreach (var r in q.Requires) needed |= r == p.Id;
            }
            if (needed) list.Add(p);
        }
        return list;
    }

    /// <summary>The checked download of a community preset (a plain FILE into <see cref="PresetEntry.SourcePath"/>);
    /// null for own and link-only presets — they are never downloaded.</summary>
    public static DownloadRequest? Request(PresetEntry e) => e.Kind != PresetKind.Community ? null
        : new DownloadRequest(new Uri(e.RawUrl), e.Sha256, e.Size, e.SourcePath, ExtractZip: false);

    private static PresetEntry Own(string id, string name, params string[] packs) => new()
    {
        Id = id, Name = name, Kind = PresetKind.Own, FileName = name + ".ini", Author = "Photo Studio",
        License = OwnLicense, LicenseUrl = OwnLicenseUrl, PageUrl = OwnPage, Packs = packs,
    };

    private static PresetEntry StarLuxe(string id, string folder, string sha, long size) => new()
    {
        Id = id, Name = "StarLuxe " + folder, Kind = PresetKind.Community, FileName = "StarLuxe " + folder + ".ini",
        Author = StarLuxeAuthor, License = "GPL-3.0", LicenseUrl = StarLuxeRepo + "/blob/" + StarLuxeCommit + "/LICENSE",
        PageUrl = StarLuxeRepo + "/tree/" + StarLuxeCommit + "/script/Presets/" + folder, Commit = StarLuxeCommit,
        RawUrl = StarLuxeRaw + folder + "/" + folder + ".ini", Sha256 = sha, Size = size, Packs = new[] { "sweetfx" },
    };

    private static PresetEntry Stella(string id, string name, string rawFile, string sha) => new()
    {
        Id = id, Name = name, Kind = PresetKind.Community, FileName = name + ".ini", Author = StellaAuthor, License = "MIT",
        LicenseUrl = StellaRepo + "/blob/" + StellaCommit + "/LICENSE",
        PageUrl = StellaRepo + "/tree/" + StellaCommit + "/public/resources/ReShade/Presets", Commit = StellaCommit,
        RawUrl = StellaRaw + rawFile, Sha256 = sha, Size = 1624, Packs = new[] { "prod80" },
        Covered = 2, Total = 3, Adjusted = true,   // MagicHDR.fx is in no pack; RESHADE_DEPTH_INPUT_* tuned for Genshin
    };

    private static PresetEntry Link(string id, string name, string author, string page, LinkReason reason) => new()
    {
        Id = id, Name = name, Kind = PresetKind.LinkOnly, Author = author, PageUrl = page, Reason = reason,
    };
}
