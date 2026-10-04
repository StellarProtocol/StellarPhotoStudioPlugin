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
    /// <summary>Enabled techniques found in the pinned packs / all enabled techniques (community only). Rule v2
    /// (2026-10-04): a technique counts when its effect FILE is in a pinned pack AND that file, compiled with the preset's
    /// own PreprocessorDefinitions (global + that effect's section), declares the TECHNIQUE name — so clones a preset's
    /// definitions create (AcerolaFX's AFX_*_COUNT) count. Measured offline, not computed here: every pinned preset is
    /// re-measured against all six packs (v1 and v2 agree on every non-AcerolaFX entry).</summary>
    public int Covered { get; init; }
    public int Total { get; init; }
    /// <summary>The installed copy leaves out settings made for another game (measured: plan D-OVR table).</summary>
    public bool Adjusted { get; init; }
    /// <summary>Definitions left out of the installed copy besides every RESHADE_DEPTH_* one (textures no pack ships).</summary>
    public IReadOnlyList<string> DropDefinitions { get; init; } = Array.Empty<string>();
    /// <summary>Values the installed copy sets wherever the key appears (any section), e.g. AcerolaFX's _MaskUI=0.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> SetValues { get; init; } = Array.Empty<KeyValuePair<string, string>>();
    public LinkReason Reason { get; init; }

    public bool Installable => Kind != PresetKind.LinkOnly;
    public bool Partial => Covered < Total;
    /// <summary>Data-folder-relative FILE path of the sha256-checked download (never written again after the download).</summary>
    public string SourcePath => "reshade/preset-sources/" + Id + ".ini";
}

/// <summary>The presets Photo Studio offers (plan 2026-10-04 presets § The catalog; licences verified at each commit).</summary>
internal static class PresetCatalog
{
    /// <summary>Licence shown for Photo Studio's own presets: the plugin repository's (owner ruling 2026-10-04 — own presets are AGPL-3.0).</summary>
    public const string OwnLicense = "AGPL-3.0-or-later";
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

    private const string AcerolaCommit = "c33f779b093fa1e25faf0c77ef22c3fe6902e2fe";
    private const string AcerolaRepo = "https://github.com/GarrettGunnell/AcerolaFX";
    // Declared before the entries that use it: static initializers run in textual order.
    private static readonly KeyValuePair<string, string>[] MaskUiOff = { new("_MaskUI", "0") };

    private const string OkamiCommit = "53e9fe085845093f50189dc5cce9419e88e423ed";
    private const string OkamiRepo = "https://github.com/MeynanAneytha/YomigamiOkami-reshade-shaders";

    // Declared before All: static initializers run in textual order.
    public static readonly PresetEntry CinematicWarm = Own("cinematic-warm", "Cinematic warm", "sweetfx", "prod80");
    public static readonly PresetEntry SoftAnime = Own("soft-anime", "Soft anime", "sweetfx", "prod80");
    public static readonly PresetEntry CoolNight = Own("cool-night", "Cool night", "sweetfx", "prod80");
    public static readonly PresetEntry CleanSharpen = Own("clean-sharpen", "Clean sharpen", "sweetfx");
    public static readonly PresetEntry DreamyGlow = Own("dreamy-glow", "Dreamy glow", "fxshaders");
    public static readonly PresetEntry LensFlare = Own("lens-flare", "Lens flare", "fxshaders");
    public static readonly PresetEntry SubjectFocus = Own("subject-focus", "Subject focus", "otisfx");   // Emphasize reads depth

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

    // AcerolaFX's own presets (owner 2026-10-04, option b — they carry the AcerolaFXStart … AcerolaFXEnd order its
    // effects need). Chosen: distinct looks, all techniques present at c33f779, no removed technique and no "+suffix"
    // duplicate (so not Hasaki). Covered counts the clone techniques the preset's own AFX_*_COUNT definitions create
    // (coverage rule v2, PresetEntry.Covered). No RESHADE_DEPTH_* and no missing texture, but they are Adjusted: the
    // installed copy turns every "Mask UI" off (_MaskUI=0). AcerolaFX was made for FFXIV, whose backbuffer alpha marks
    // UI; this game's alpha is 1 everywhere, so with Mask UI on, AcerolaFXEnd (and CRT/Dither) restore the original
    // frame and the look disappears (found + confirmed in game 2026-10-04).
    public static readonly PresetEntry AcerolaGameplay = Acerola("acerolafx-gameplay", "Gameplay", "GameplayLowest",
        "b6f353924a8bb8755506fafaa043bb4f026fa87984ffb616649b7b0c6499c6c1", 3451, 9);   // the only one without XeGTAO
    public static readonly PresetEntry AcerolaGoldenAge = Acerola("acerolafx-golden-age", "Golden Age", "GoldenAge",
        "f541340d06e8596374f0f6c8e0d6e2b81bf1d22bd13045030398ff7a6a4b1ff4", 8210, 10);
    public static readonly PresetEntry AcerolaDraft = Acerola("acerolafx-draft", "Draft", "Draft",
        "b8911c98c07b6a4b025afdee8ab6a4c99340b01f840624d088c30680f8feb53f", 5746, 7);
    public static readonly PresetEntry AcerolaDistantPast = Acerola("acerolafx-distant-past", "Distant Past", "DistantPast",
        "2dd34d8df4eeb0fd0b49a4e01cf30a518bed9f452980acbfd6c347f5502679a5", 4195, 18);

    public static readonly PresetEntry IpsuShade = Link("ipsushade", "IpsuShade", "ipsusu", "https://github.com/ipsusu/IpsuShade", LinkReason.AskAuthor);
    public static readonly PresetEntry Steaxs = Link("steaxs-filter-pack", "STEAXS Filter Pack", "steaxss",
        "https://github.com/steaxss/STEAXS-FILTER-PACK", LinkReason.NoLicence);
    public static readonly PresetEntry NoRange = Link("norange-wuwa", "NoRange (WuWa)", "No Range",
        "https://gamebanana.com/tools/18631", LinkReason.AskAuthor);
    public static readonly PresetEntry EndfieldOfficial = Link("endfield-official-filter", "Official Filter (Endfield)", "Eclyse069",
        "https://gamebanana.com/mods/654035", LinkReason.AskAuthor);
    public static readonly PresetEntry VibrantSharpen = Link("vibrantsharpen-zzz", "VibrantSharpen (ZZZ)", "Yamilowo",
        "https://gamebanana.com/mods/607194", LinkReason.AskAuthor);

    public static IReadOnlyList<PresetEntry> All { get; } = new[]
    {
        CinematicWarm, SoftAnime, CoolNight, CleanSharpen, DreamyGlow, LensFlare, SubjectFocus,
        StarLuxeGalactic, StarLuxeLegacy, StarLuxeLuminescence, StellaMedium, StellaHigh, OkamiCityRuins,
        AcerolaGameplay, AcerolaGoldenAge, AcerolaDraft, AcerolaDistantPast,
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
        Id = id, Name = name, Kind = PresetKind.Community, FileName = name + ".ini", Author = StellaAuthor, License = "CC-BY-SA-4.0",
        LicenseUrl = StellaRepo + "/blob/" + StellaCommit + "/public/resources/ReShade/Presets/LICENSE",
        PageUrl = StellaRepo + "/tree/" + StellaCommit + "/public/resources/ReShade/Presets", Commit = StellaCommit,
        RawUrl = StellaRaw + rawFile, Sha256 = sha, Size = 1624, Packs = new[] { "prod80", "fxshaders" },
        Covered = 3, Total = 3, Adjusted = true,   // MagicHDR@MagicHDR.fx is FXShaders'; RESHADE_DEPTH_INPUT_* tuned for Genshin
    };

    private static PresetEntry Acerola(string id, string name, string file, string sha, long size, int techniques) => new()
    {
        Id = id, Name = "AcerolaFX " + name, Kind = PresetKind.Community, FileName = "AcerolaFX " + name + ".ini",
        Author = "Garrett Gunnell (AcerolaFX)", License = "MIT", LicenseUrl = AcerolaRepo + "/blob/" + AcerolaCommit + "/LICENSE.md",
        PageUrl = AcerolaRepo + "/tree/" + AcerolaCommit + "/Presets", Commit = AcerolaCommit,
        RawUrl = "https://raw.githubusercontent.com/GarrettGunnell/AcerolaFX/" + AcerolaCommit + "/Presets/AcerolaFX_" + file + ".ini",
        Sha256 = sha, Size = size, Packs = new[] { "acerolafx" }, Covered = techniques, Total = techniques,
        Adjusted = true, SetValues = MaskUiOff,
    };

    private static PresetEntry Link(string id, string name, string author, string page, LinkReason reason) => new()
    {
        Id = id, Name = name, Kind = PresetKind.LinkOnly, Author = author, PageUrl = page, Reason = reason,
    };
}
