using System;
using System.Collections.Generic;
using System.Threading;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

public sealed class PackInstallerTests
{
    private readonly FakeDownloads _dl = new();
    private readonly HashSet<string> _dirs = new();
    private int _dirChecks;
    private readonly List<string> _warnings = new();

    private PackInstaller Make()
    {
        SynchronizationContext.SetSynchronizationContext(null);   // continuations run inline when a fake completes
        return new PackInstaller(_dl, PackCatalog.All, d => { _dirChecks++; return _dirs.Contains(d); }, _warnings.Add);
    }

    private void Extract(ShaderPack p) => _dirs.Add(PackCatalog.EffectsFolder("/data", p));

    private void Succeed(int call, ShaderPack p)
    {
        Extract(p);
        _dl.Calls[call].Done.SetResult(new DownloadResult(true, "/data/" + p.TargetPath, null));
    }

    [Fact]
    public void Disk_state_is_read_at_start_and_never_per_status_call()
    {
        _dirs.Add(PackCatalog.EffectsFolder("/data", PackCatalog.Standard));
        _dirs.Add(PackCatalog.PackFolder("/data", PackCatalog.SweetFx));   // an older commit's folder only
        var i = Make();
        var checks = _dirChecks;
        Assert.Equal(PackStatus.Installed, i.Status(PackCatalog.Standard));
        Assert.Equal(PackStatus.UpdateAvailable, i.Status(PackCatalog.SweetFx));
        Assert.Equal(PackStatus.NotInstalled, i.Status(PackCatalog.Prod80));
        Assert.Equal(checks, _dirChecks);
    }

    [Fact]
    public void Requesting_sweetfx_downloads_standard_first_then_sweetfx()
    {
        var i = Make();
        var changed = 0;
        i.PacksChanged += () => changed++;
        i.Request(PackCatalog.SweetFx);
        Assert.Single(_dl.Calls);
        Assert.Equal("reshade/packs/standard", _dl.Calls[0].Request.TargetPath);
        Assert.Equal(PackStatus.Downloading, i.Status(PackCatalog.Standard));
        Assert.Equal(PackStatus.Queued, i.Status(PackCatalog.SweetFx));
        Succeed(0, PackCatalog.Standard);
        Assert.Equal(2, _dl.Calls.Count);
        Assert.Equal("reshade/packs/sweetfx", _dl.Calls[1].Request.TargetPath);
        Succeed(1, PackCatalog.SweetFx);
        Assert.Equal(PackStatus.Installed, i.Status(PackCatalog.Standard));
        Assert.Equal(PackStatus.Installed, i.Status(PackCatalog.SweetFx));
        Assert.Equal(2, changed);
    }

    [Fact]
    public void An_installed_requirement_is_not_downloaded_again()
    {
        Extract(PackCatalog.Standard);
        var i = Make();
        i.Request(PackCatalog.Prod80);
        Assert.Single(_dl.Calls);
        Assert.Equal("reshade/packs/prod80", _dl.Calls[0].Request.TargetPath);
        Assert.Equal(PackCatalog.Prod80.Sha256, _dl.Calls[0].Request.Sha256);
        Assert.Equal(PackCatalog.Prod80.Size, _dl.Calls[0].Request.MaxBytes);
    }

    [Fact]
    public void A_failed_requirement_fails_the_queued_pack_without_downloading_it()
    {
        var i = Make();
        i.Request(PackCatalog.SweetFx);
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Single(_dl.Calls);
        Assert.Equal(PackStatus.Failed, i.Status(PackCatalog.Standard));
        Assert.Equal(PackStatus.Failed, i.Status(PackCatalog.SweetFx));
        Assert.Equal("checksum mismatch", i.Error(PackCatalog.SweetFx));
        Assert.Single(_warnings);
    }

    [Fact]
    public void Progress_shows_while_downloading_and_a_late_value_after_success_is_ignored()
    {
        var i = Make();
        i.Request(PackCatalog.Standard);
        _dl.Calls[0].Progress!.Report(0.4);
        Assert.Equal(0.4, i.Progress(PackCatalog.Standard), 3);
        Succeed(0, PackCatalog.Standard);
        _dl.Calls[0].Progress!.Report(1.0);   // IPluginDownloads may deliver a final 1.0 after completion
        Assert.Equal(PackStatus.Installed, i.Status(PackCatalog.Standard));
        Assert.Equal(0d, i.Progress(PackCatalog.Standard));
    }

    [Fact]
    public void A_second_request_while_queued_does_not_queue_twice()
    {
        var i = Make();
        i.Request(PackCatalog.Standard);
        i.Request(PackCatalog.Standard);
        Succeed(0, PackCatalog.Standard);
        Assert.Single(_dl.Calls);
    }

    [Fact]
    public void Search_paths_list_installed_packs_in_catalog_order_with_their_textures()
    {
        Extract(PackCatalog.Prod80);
        Extract(PackCatalog.Standard);
        _dirs.Add(PackCatalog.TexturesFolder("/data", PackCatalog.Standard));
        var (fx, tex) = Make().SearchPaths();
        Assert.Equal(new[] { PackCatalog.EffectsFolder("/data", PackCatalog.Standard), PackCatalog.EffectsFolder("/data", PackCatalog.Prod80) }, fx);
        Assert.Equal(new[] { PackCatalog.TexturesFolder("/data", PackCatalog.Standard) }, tex);
    }

    [Fact]
    public void Dispose_cancels_the_running_download_without_marking_it_failed()
    {
        var i = Make();
        i.Request(PackCatalog.Standard);
        i.Dispose();
        Assert.Equal(PackStatus.NotInstalled, i.Status(PackCatalog.Standard));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void A_failed_pack_raises_PackFailed_once_after_its_status_is_failed()
    {
        var i = Make();
        var seen = new List<(string Id, PackStatus Status)>();
        i.PackFailed += p => seen.Add((p.Id, i.Status(p)));
        i.Request(PackCatalog.SweetFx);
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Equal(new[] { ("standard", PackStatus.Failed) }, seen);   // SweetFX fails with it (FailDependents) — no event of its own
        Assert.Equal(PackStatus.Failed, i.Status(PackCatalog.SweetFx));
    }

    // Review carry-over (d): one throwing PackFailed subscriber must not stop PumpAsync from reaching the next,
    // independently-queued pack. Standard is already installed so SweetFX and Prod80 (leaves, unrelated to each other)
    // queue without pulling Standard in again; SweetFX's own failure cannot cascade to Prod80 via FailDependents (Prod80
    // does not require SweetFX), so Prod80 is still sitting in the queue when the throwing subscriber fires.
    [Fact]
    public void A_throwing_PackFailed_subscriber_does_not_stop_the_pump_from_reaching_the_next_pack()
    {
        _dirs.Add(PackCatalog.EffectsFolder("/data", PackCatalog.Standard));
        var i = Make();
        i.PackFailed += _ => throw new InvalidOperationException("boom");
        i.Request(PackCatalog.SweetFx);
        i.Request(PackCatalog.Prod80);
        Assert.Single(_dl.Calls);   // only SweetFX has started; Prod80 is queued behind it
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Equal(PackStatus.Failed, i.Status(PackCatalog.SweetFx));
        Assert.Equal(2, _dl.Calls.Count);   // the pump reached Prod80 despite the throwing subscriber
        Assert.Equal("reshade/packs/prod80", _dl.Calls[1].Request.TargetPath);
    }

    // Review fix round 2, item 3: PacksChanged (the SUCCESS event) needs the same per-subscriber try/catch as
    // PackFailed already has — a throwing subscriber here used to propagate out of InstallOneAsync's success branch,
    // straight into PumpAsync's catch(Exception), which stops the pump (_pumping=false) before the next queued pack
    // (Prod80, already sitting in the queue) is ever reached.
    [Fact]
    public void A_throwing_PacksChanged_subscriber_does_not_stop_the_pump_from_reaching_the_next_pack()
    {
        _dirs.Add(PackCatalog.EffectsFolder("/data", PackCatalog.Standard));
        var i = Make();
        i.PacksChanged += () => throw new InvalidOperationException("boom");
        i.Request(PackCatalog.SweetFx);
        i.Request(PackCatalog.Prod80);
        Assert.Single(_dl.Calls);   // only SweetFX has started; Prod80 is queued behind it
        Succeed(0, PackCatalog.SweetFx);
        Assert.Equal(PackStatus.Installed, i.Status(PackCatalog.SweetFx));
        Assert.Equal(2, _dl.Calls.Count);   // the pump reached Prod80 despite the throwing subscriber
        Assert.Equal("reshade/packs/prod80", _dl.Calls[1].Request.TargetPath);
    }

    // Review fix round 2, item 4: FailureKind classifies a Failed pack's error text for a player-friendly message.
    // Every InlineData string is the FRAMEWORK's own, exact error text (PluginDownloadService.cs / MapError), not a
    // guess — "checksum mismatch" and "too large" both mean the upstream file no longer matches what we pinned
    // (Changed); "network error" and "timed out" mean the network (Network); everything else, including the
    // framework's other literal texts, is Other.
    // NOTE (deviation, precedented in ReShadeViewTests.cs / PresetCatalogTests.cs): DownloadFailure is `internal`; a
    // `public` xunit Theory method taking it as a parameter is CS0051 even with InternalsVisibleTo. Indirected through
    // nameof()/ToString(), the same workaround used for LinkReason and ReShadePanel.
    [Theory]
    [InlineData("checksum mismatch", nameof(DownloadFailure.Changed))]
    [InlineData("too large", nameof(DownloadFailure.Changed))]
    [InlineData("network error", nameof(DownloadFailure.Network))]
    [InlineData("timed out", nameof(DownloadFailure.Network))]
    [InlineData("busy", nameof(DownloadFailure.Other))]
    [InlineData("invalid request", nameof(DownloadFailure.Other))]
    [InlineData("https only", nameof(DownloadFailure.Other))]
    [InlineData("invalid size", nameof(DownloadFailure.Other))]
    [InlineData("bad zip", nameof(DownloadFailure.Other))]
    [InlineData("could not write to the data folder", nameof(DownloadFailure.Other))]
    [InlineData("download failed unexpectedly", nameof(DownloadFailure.Other))]
    public void FailureKind_classifies_the_frameworks_own_error_strings(string error, string expected)
    {
        var i = Make();
        i.Request(PackCatalog.Standard);
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, error));
        Assert.Equal(expected, i.FailureKind(PackCatalog.Standard).ToString());
    }

    [Fact]
    public void FailureKind_is_None_when_the_pack_has_not_failed()
    {
        var i = Make();
        Assert.Equal(DownloadFailure.None, i.FailureKind(PackCatalog.Standard));
        i.Request(PackCatalog.Standard);
        Assert.Equal(DownloadFailure.None, i.FailureKind(PackCatalog.Standard));   // Queued, not Failed
    }

    [Fact]
    public void FailureKind_is_Requirement_when_it_failed_because_its_own_requirement_failed()
    {
        var i = Make();
        i.Request(PackCatalog.SweetFx);
        // SweetFX never gets its own download attempt: standard (its requirement) fails first and FailDependents
        // marks SweetFX Failed directly — Requirement must win even though the underlying text would read as Changed.
        _dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Equal(DownloadFailure.Requirement, i.FailureKind(PackCatalog.SweetFx));
    }
}
