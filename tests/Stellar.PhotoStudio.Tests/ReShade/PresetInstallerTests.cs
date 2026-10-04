using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

// Spec § 12 V4: installing a preset queues its packs first (through PackInstaller), then writes ours / downloads the
// pinned community file; link-only presets are never downloaded; an installed file is never overwritten; the checked
// download is never rewritten (plan D-OVR).
public sealed class PresetInstallerTests
{
    private readonly FakeDownloads _dl = new();
    private readonly HashSet<string> _dirs = new();
    private readonly FakeReShadePresetFiles _files = new();
    private readonly List<string> _warnings = new();

    private PresetInstaller Make()
    {
        SynchronizationContext.SetSynchronizationContext(null);   // continuations run inline when a fake completes
        var serial = new SerialDownloads(_dl);
        var packs = new PackInstaller(serial, PackCatalog.All, _dirs.Contains, _warnings.Add);
        return new PresetInstaller(serial, packs, PresetCatalog.All, _files, _warnings.Add);
    }

    private static string Installed(PresetEntry e) => Path.Combine("/data", "reshade", "presets", e.FileName);
    private static string Source(PresetEntry e) => "/data/" + e.SourcePath;

    private void PackDone(int call, ShaderPack p)
    {
        _dirs.Add(PackCatalog.EffectsFolder("/data", p));
        _dl.Calls[call].Done.SetResult(new DownloadResult(true, "/data/" + p.TargetPath, null));
    }

    private void PresetDone(int call, PresetEntry e, string text)
    {
        _files.Files[Source(e)] = text;   // what IPluginDownloads wrote after the sha256 check
        _dl.Calls[call].Done.SetResult(new DownloadResult(true, Source(e), null));
    }

    private void HavePacks(params ShaderPack[] packs)
    {
        foreach (var p in packs) _dirs.Add(PackCatalog.EffectsFolder("/data", p));
    }

    [Fact]
    public void Community_preset_downloads_its_packs_first_then_the_pinned_file()
    {
        var i = Make();
        var changed = 0;
        i.PresetsChanged += () => changed++;
        var g = PresetCatalog.StarLuxeGalactic;
        i.Request(g);
        Assert.Equal(PresetStatus.Queued, i.Status(g));
        Assert.Equal("reshade/packs/standard", _dl.Calls[0].Request.TargetPath);
        PackDone(0, PackCatalog.Standard);
        Assert.Equal("reshade/packs/sweetfx", _dl.Calls[1].Request.TargetPath);
        Assert.Equal(PresetStatus.Queued, i.Status(g));   // still waiting for SweetFX
        PackDone(1, PackCatalog.SweetFx);
        Assert.Equal(3, _dl.Calls.Count);
        var r = _dl.Calls[2].Request;
        Assert.Equal(g.RawUrl, r.Url.AbsoluteUri);
        Assert.Equal(g.Sha256, r.Sha256);
        Assert.Equal(g.Size, r.MaxBytes);
        Assert.False(r.ExtractZip);
        Assert.Equal("reshade/preset-sources/starluxe-galactic.ini", r.TargetPath);
        Assert.Equal(PresetStatus.Downloading, i.Status(g));
        PresetDone(2, g, "Techniques=Curves@Curves.fx\n");
        Assert.Equal(PresetStatus.Installed, i.Status(g));
        Assert.Equal("Techniques=Curves@Curves.fx\n", _files.Files[Installed(g)]);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Own_preset_is_written_from_the_plugin_after_its_packs_and_never_downloaded()
    {
        var i = Make();
        var c = PresetCatalog.CinematicWarm;
        i.Request(c);
        PackDone(0, PackCatalog.Standard);
        PackDone(1, PackCatalog.SweetFx);
        PackDone(2, PackCatalog.Prod80);
        Assert.Equal(new[] { "reshade/packs/standard", "reshade/packs/sweetfx", "reshade/packs/prod80" },
            _dl.Calls.Select(x => x.Request.TargetPath).ToArray());   // three packs; nothing for the preset itself
        Assert.Equal(PresetStatus.Installed, i.Status(c));
        Assert.Equal(OwnPresets.Text("cinematic-warm"), _files.Files[Installed(c)]);
    }

    [Fact]
    public void Installed_packs_are_not_downloaded_again()
    {
        HavePacks(PackCatalog.Standard, PackCatalog.SweetFx);
        var i = Make();
        i.Request(PresetCatalog.CleanSharpen);
        Assert.Empty(_dl.Calls);
        Assert.Equal(PresetStatus.Installed, i.Status(PresetCatalog.CleanSharpen));
    }

    [Fact]
    public void A_failed_pack_fails_the_waiting_preset_and_nothing_more_is_downloaded()
    {
        var i = Make();
        var g = PresetCatalog.StarLuxeGalactic;
        i.Request(g);
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Equal(PresetStatus.Failed, i.Status(g));
        Assert.Equal("ReShade standard", i.FailedRequirement(g));
        Assert.Equal("checksum mismatch", i.Error(g));
        Assert.Single(_dl.Calls);
        Assert.Empty(_files.Writes);
    }

    // Review carry-over (b): StarLuxe Galactic's own Packs list is just ["sweetfx"] — "standard" reaches it only through
    // PresetCatalog.PacksWithRequires (SweetFX's own Requires). The test above already proves this end to end (a
    // "standard" failure fails the sweetfx-only preset); this one pins the same fact against a second sweetfx-only
    // preset, so a future change that reads e.Packs directly instead of PacksWithRequires(e) is caught here too.
    [Fact]
    public void A_standard_failure_fails_a_sweetfx_only_preset_via_PacksWithRequires_not_its_own_Packs_list()
    {
        var i = Make();
        var legacy = PresetCatalog.StarLuxeLegacy;
        Assert.Equal(new[] { "sweetfx" }, legacy.Packs);   // "standard" is NOT in the preset's own Packs
        Assert.Contains(PackCatalog.Standard, PresetCatalog.PacksWithRequires(legacy));   // but IS in PacksWithRequires
        i.Request(legacy);
        Assert.Equal("reshade/packs/standard", _dl.Calls[0].Request.TargetPath);
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Equal(PresetStatus.Failed, i.Status(legacy));
        Assert.Equal("ReShade standard", i.FailedRequirement(legacy));
    }

    [Fact]
    public void Retry_after_a_failure_starts_over()
    {
        var i = Make();
        var g = PresetCatalog.StarLuxeGalactic;
        i.Request(g);
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "offline"));
        i.Request(g);
        Assert.Equal(PresetStatus.Queued, i.Status(g));
        Assert.Equal("reshade/packs/standard", _dl.Calls[1].Request.TargetPath);
    }

    [Fact]
    public void A_failed_preset_download_shows_its_error_and_writes_nothing()
    {
        HavePacks(PackCatalog.Standard, PackCatalog.Prod80, PackCatalog.FxShaders);   // Stella: prod80 + FXShaders (MagicHDR)
        var i = Make();
        var s = PresetCatalog.StellaMedium;
        i.Request(s);
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Equal(PresetStatus.Failed, i.Status(s));
        Assert.Null(i.FailedRequirement(s));
        Assert.Equal("checksum mismatch", i.Error(s));
        Assert.Empty(_files.Writes);
    }

    [Fact]
    public void Link_only_presets_are_never_downloaded()
    {
        var i = Make();
        foreach (var e in PresetCatalog.All.Where(e => e.Kind == PresetKind.LinkOnly))
        {
            i.Request(e);
            Assert.Equal(PresetStatus.NotInstalled, i.Status(e));
        }
        Assert.Empty(_dl.Calls);   // no pack, no file
        Assert.Empty(_files.Writes);
    }

    [Fact]
    public void An_existing_preset_file_is_never_overwritten()
    {
        var c = PresetCatalog.SoftAnime;
        _files.Files[Installed(c)] = "the player's edits";
        HavePacks(PackCatalog.Standard, PackCatalog.SweetFx, PackCatalog.Prod80);
        var i = Make();
        Assert.Equal(PresetStatus.Installed, i.Status(c));
        i.Request(c);
        Assert.Equal("the player's edits", _files.Files[Installed(c)]);
        Assert.Empty(_files.Writes);
    }

    [Fact]
    public void A_file_that_appears_while_installing_is_kept()
    {
        var i = Make();
        var c = PresetCatalog.CleanSharpen;
        i.Request(c);
        _files.Files[Installed(c)] = "copied in by hand";
        PackDone(0, PackCatalog.Standard);
        PackDone(1, PackCatalog.SweetFx);
        Assert.Equal("copied in by hand", _files.Files[Installed(c)]);
        Assert.Equal(PresetStatus.Installed, i.Status(c));
    }

    [Fact]
    public void The_checked_download_is_never_rewritten_and_the_installed_copy_drops_other_game_settings()
    {
        HavePacks(PackCatalog.Standard, PackCatalog.Prod80, PackCatalog.FxShaders);   // Stella: prod80 + FXShaders (MagicHDR)
        var i = Make();
        var s = PresetCatalog.StellaMedium;
        const string text = "PreprocessorDefinitions=RESHADE_DEPTH_INPUT_IS_REVERSED=1,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=1,BLOOM_QUALITY_0_TO_2=2\n"
            + "Techniques=prod80_02_Bloom@PD80_02_Bloom.fx\n";
        i.Request(s);
        PresetDone(0, s, text);
        Assert.Equal(text, _files.Files[Source(s)]);   // byte for byte what was sha256-checked
        Assert.Equal(new[] { Installed(s) }, _files.Writes);   // the only file Photo Studio wrote
        Assert.Equal("PreprocessorDefinitions=BLOOM_QUALITY_0_TO_2=2\nTechniques=prod80_02_Bloom@PD80_02_Bloom.fx\n",
            _files.Files[Installed(s)]);
    }

    [Fact]
    public void Disk_is_read_at_start_and_on_Rescan_only()
    {
        var i = Make();
        var after = _files.ExistsCalls;
        Assert.Equal(14, after);   // the 14 installable entries
        foreach (var e in PresetCatalog.All) i.Status(e);
        Assert.Equal(after, _files.ExistsCalls);
        _files.Files[Installed(PresetCatalog.CoolNight)] = "x";
        Assert.Equal(PresetStatus.NotInstalled, i.Status(PresetCatalog.CoolNight));
        i.Rescan();
        Assert.Equal(PresetStatus.Installed, i.Status(PresetCatalog.CoolNight));
    }

    [Fact]
    public void A_pack_the_player_starts_while_a_preset_downloads_waits_its_turn()
    {
        HavePacks(PackCatalog.Standard, PackCatalog.SweetFx);
        SynchronizationContext.SetSynchronizationContext(null);
        var serial = new SerialDownloads(_dl);
        var packs = new PackInstaller(serial, PackCatalog.All, _dirs.Contains, _warnings.Add);
        var i = new PresetInstaller(serial, packs, PresetCatalog.All, _files, _warnings.Add);
        i.Request(PresetCatalog.StarLuxeLegacy);
        packs.Request(PackCatalog.Prod80);
        Assert.Single(_dl.Calls);   // prod80 queued behind the preset — never a "busy" overlap
        PresetDone(0, PresetCatalog.StarLuxeLegacy, "Techniques=Tint@Sepia.fx\n");
        Assert.Equal("reshade/packs/prod80", _dl.Calls[1].Request.TargetPath);
    }

    // Review carry-over (d): a sibling subscriber on the SAME PackInstaller.PackFailed event that throws must not stop
    // PresetInstaller's own subscriber (FailWaiting) from running — PackInstaller now invokes each subscriber in its own
    // try/catch, and FailWaiting itself is wrapped defensively too.
    [Fact]
    public void A_throwing_sibling_PackFailed_subscriber_does_not_stop_the_preset_from_being_marked_failed()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var serial = new SerialDownloads(_dl);
        var packs = new PackInstaller(serial, PackCatalog.All, _dirs.Contains, _warnings.Add);
        packs.PackFailed += _ => throw new System.InvalidOperationException("boom");   // a hostile sibling, attached first
        var i = new PresetInstaller(serial, packs, PresetCatalog.All, _files, _warnings.Add);
        var g = PresetCatalog.StarLuxeGalactic;
        i.Request(g);
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Equal(PresetStatus.Failed, i.Status(g));
        Assert.Equal("ReShade standard", i.FailedRequirement(g));
    }

    // Fix round 1, minor 2: a throwing PresetsChanged subscriber sits inside InstallAsync's own try block, right after
    // the write — unguarded, its exception used to be caught by InstallAsync's OWN catch(Exception), which then called
    // Fail(e, s, ...) and logged "install failed" for a preset whose file had already been written successfully. By then
    // _live.Remove(e.Id) and _disk[e.Id]=true had already run, so Status()/Error() still read Installed/null either way
    // (they fall through to the already-updated disk cache once the live entry is gone) — the observable bug is the
    // spurious warning, not the reported status.
    [Fact]
    public void A_throwing_PresetsChanged_subscriber_does_not_log_install_failed_for_the_preset_that_installed()
    {
        var i = Make();
        i.PresetsChanged += () => throw new System.InvalidOperationException("boom");
        var c = PresetCatalog.CleanSharpen;
        i.Request(c);
        PackDone(0, PackCatalog.Standard);
        PackDone(1, PackCatalog.SweetFx);
        Assert.Equal(PresetStatus.Installed, i.Status(c));
        Assert.Null(i.Error(c));
        Assert.Equal(OwnPresets.Text("clean-sharpen"), _files.Files[Installed(c)]);
        Assert.DoesNotContain(_warnings, w => w.Contains("install failed"));
        Assert.Contains(_warnings, w => w.Contains("PresetsChanged subscriber threw"));
    }

    // Review fix round 2, item 3: a required pack stuck neither installed nor queued/downloading (e.g. UpdateAvailable)
    // must fail the waiting preset instead of leaving it Queued forever. A pack can land there if its in-flight
    // download is cancelled (PackInstaller.Dispose(), e.g. the plugin unloading mid-download): InstallOneAsync's
    // OperationCanceledException catch removes the live entry WITHOUT raising PackFailed, so Status() falls back to
    // the stale disk read taken at construction. Nothing re-evaluates the now-stuck preset automatically — until
    // StartReady runs again for ANY reason (here, requesting a second, unrelated, already-installed-packs preset),
    // since it always re-walks the WHOLE catalog, not just the one just requested.
    [Fact]
    public void A_required_pack_stuck_neither_installed_nor_progressing_fails_the_waiting_preset()
    {
        _dirs.Add(PackCatalog.EffectsFolder("/data", PackCatalog.Standard));
        _dirs.Add(PackCatalog.PackFolder("/data", PackCatalog.SweetFx));   // a stale/partial folder -> UpdateAvailable
        _dirs.Add(PackCatalog.EffectsFolder("/data", PackCatalog.Prod80));
        _dirs.Add(PackCatalog.EffectsFolder("/data", PackCatalog.FxShaders));
        SynchronizationContext.SetSynchronizationContext(null);
        var serial = new SerialDownloads(_dl);
        var packs = new PackInstaller(serial, PackCatalog.All, _dirs.Contains, _warnings.Add);
        var i = new PresetInstaller(serial, packs, PresetCatalog.All, _files, _warnings.Add);
        var c = PresetCatalog.CleanSharpen;   // needs [standard, sweetfx]; standard installed, sweetfx UpdateAvailable
        i.Request(c);
        // Request() itself re-queues SweetFX immediately (UpdateAvailable != Installed) — not stuck yet.
        Assert.Equal(PackStatus.Downloading, packs.Status(PackCatalog.SweetFx));
        Assert.Equal(PresetStatus.Queued, i.Status(c));
        // The plugin unloads mid-download: cancel SweetFX's in-flight download without ever raising PackFailed.
        packs.Dispose();
        Assert.Equal(PackStatus.UpdateAvailable, packs.Status(PackCatalog.SweetFx));   // reverted to the stale disk read
        Assert.Equal(PresetStatus.Queued, i.Status(c));   // nothing has told it yet
        // Requesting an unrelated preset whose packs are ALL already installed re-walks the whole catalog in
        // StartReady, which must now notice SweetFX is stuck and fail c instead of leaving it Queued forever.
        var other = PresetCatalog.StellaMedium;   // needs [standard, prod80, fxshaders] only — never touches sweetfx
        i.Request(other);
        Assert.Equal(PresetStatus.Failed, i.Status(c));
        Assert.Equal("SweetFX", i.FailedRequirement(c));
    }

    // Review fix round 2, item 4: FailureKind classifies a Failed preset's error text the same way PackInstaller's
    // does. Each InlineData string is the framework's own exact error text (PluginDownloadService.cs).
    // NOTE (deviation, precedented): DownloadFailure is `internal`; a `public` Theory parameter of that type is
    // CS0051 even with InternalsVisibleTo — indirected through nameof()/ToString() instead.
    [Theory]
    [InlineData("checksum mismatch", nameof(DownloadFailure.Changed))]
    [InlineData("too large", nameof(DownloadFailure.Changed))]
    [InlineData("network error", nameof(DownloadFailure.Network))]
    [InlineData("timed out", nameof(DownloadFailure.Network))]
    [InlineData("bad zip", nameof(DownloadFailure.Other))]
    public void FailureKind_classifies_a_failed_presets_own_download_error(string error, string expected)
    {
        HavePacks(PackCatalog.Standard, PackCatalog.Prod80, PackCatalog.FxShaders);   // Stella: prod80 + FXShaders (MagicHDR)
        var i = Make();
        var s = PresetCatalog.StellaMedium;
        i.Request(s);
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, error));
        Assert.Equal(expected, i.FailureKind(s).ToString());
    }

    [Fact]
    public void FailureKind_is_Requirement_when_a_required_pack_failed()
    {
        var i = Make();
        var g = PresetCatalog.StarLuxeGalactic;
        i.Request(g);
        // The pack's own error text ("checksum mismatch") would otherwise read as Changed — Requirement must win.
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Equal(DownloadFailure.Requirement, i.FailureKind(g));
    }

    [Fact]
    public void FailureKind_is_None_before_any_request_or_while_still_in_progress()
    {
        var i = Make();
        Assert.Equal(DownloadFailure.None, i.FailureKind(PresetCatalog.StellaMedium));
        i.Request(PresetCatalog.StarLuxeGalactic);
        Assert.Equal(DownloadFailure.None, i.FailureKind(PresetCatalog.StarLuxeGalactic));   // Queued, not Failed
    }
}
