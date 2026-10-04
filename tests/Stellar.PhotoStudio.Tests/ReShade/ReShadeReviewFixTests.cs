using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Presets;
using Stellar.PhotoStudio.ReShade;
using Xunit;

namespace Stellar.PhotoStudio.Tests.ReShade;

/// <summary>Photo Studio 1.5.0 review fix round (logic): owner ruling O1 (closing restores ReShade), ReShade's own
/// preset kept as a full path, the settle reads, the pack "needs" failure and the shutter gate's disarm.</summary>
public sealed class ReShadeReviewFixTests
{
    private const float Frame = 1f / 60f;
    private const string Folder = "/data/reshade/presets";
    private const string Own = @"C:\g\ReShadePreset.ini";       // ReShade's own preset, outside our folder
    private static readonly ReShadeTechnique Mxao = new("MXAO", "MXAO.fx", Enabled: false, UsesDepth: true);
    private readonly FakeReShade _fake = new() { CurrentPreset = Own };

    private ReShadeControl Make()
    {
        _fake.List.Add(Mxao);
        return new ReShadeControl(_fake, Folder);
    }

    /// <summary>ReShade "applies" every request it got so far, then raises Changed.</summary>
    private void Land(ReShadeControl c)
    {
        if (_fake.EnabledRequests.Count > 0) _fake.EnabledNow = _fake.EnabledRequests[^1];
        if (_fake.PresetCalls.Count > 0) _fake.CurrentPreset = _fake.PresetCalls[^1];
        c.OnChanged();
        c.Tick(Frame);
    }

    // ── O1: closing Photo Studio returns ReShade to what it was when Photo Studio opened ──────────────────────────

    [Fact]
    public void Close_restores_the_on_off_and_preset_changed_while_open()
    {
        var c = Make();
        c.OnStudioOpened();
        c.SetEnabled(false);
        c.SetPresetPath(Folder + "/Noir.ini");
        Land(c);
        _fake.EnabledRequests.Clear();
        _fake.PresetCalls.Clear();

        c.OnStudioClosed();

        Assert.Equal(new[] { true }, _fake.EnabledRequests);
        Assert.Equal(new[] { Own }, _fake.PresetCalls);           // the player's own preset, full path
    }

    [Fact]
    public void Close_without_a_change_sends_nothing()
    {
        var c = Make();
        c.OnStudioOpened();
        c.OnStudioClosed();
        Assert.Empty(_fake.EnabledRequests);
        Assert.Empty(_fake.PresetCalls);
    }

    [Fact]
    public void Close_restores_only_what_differs()
    {
        var c = Make();
        c.OnStudioOpened();
        c.SetEnabled(false);
        Land(c);
        _fake.EnabledRequests.Clear();
        c.OnStudioClosed();
        Assert.Equal(new[] { true }, _fake.EnabledRequests);
        Assert.Empty(_fake.PresetCalls);
    }

    [Fact]
    public void Close_while_reshade_is_not_ready_keeps_the_snapshot_and_restores_at_the_next_close()
    {
        var c = Make();
        c.OnStudioOpened();
        c.SetEnabled(false);
        Land(c);
        _fake.EnabledRequests.Clear();
        _fake.State = ReShadeState.Loading;
        c.OnStudioClosed();
        Assert.Empty(_fake.EnabledRequests);                      // nothing sent while not Ready
        _fake.State = ReShadeState.Ready;
        c.OnStudioOpened();                                       // the kept snapshot is NOT replaced by the edited state
        c.OnStudioClosed();
        Assert.Equal(new[] { true }, _fake.EnabledRequests);
    }

    [Fact]
    public void A_second_close_after_a_restore_sends_nothing_more()
    {
        var c = Make();
        c.OnStudioOpened();
        c.SetEnabled(false);
        Land(c);
        c.OnStudioClosed();
        Land(c);
        _fake.EnabledRequests.Clear();
        c.OnStudioClosed();
        Assert.Empty(_fake.EnabledRequests);
    }

    // ── qa major: ReShade's own preset (outside our presets folder) is remembered as its FULL path in memory ────────

    [Fact]
    public void Current_keeps_reshades_own_preset_as_a_full_path_and_apply_goes_back_to_it()
    {
        var c = Make();
        var choice = c.Current()!;
        Assert.Equal(Own, choice.Preset);
        c.SetPresetPath(Folder + "/Noir.ini");
        Land(c);
        _fake.PresetCalls.Clear();
        c.Apply(choice);
        Assert.Equal(new[] { Own }, _fake.PresetCalls);           // not "<our folder>/ReShadePreset.ini" (an empty preset)
    }

    [Fact]
    public void Apply_of_a_bare_file_name_still_resolves_in_our_presets_folder()
    {
        var c = Make();
        c.Apply(new ReShadeChoice("Noir.ini", true));
        Assert.Equal(System.IO.Path.Combine(Folder, "Noir.ini"), _fake.PresetCalls.Single());
    }

    [Fact]
    public void Reset_all_restores_the_players_own_reshade_preset()
    {
        var c = Make();
        var store = new PresetStore(new MemFiles(), _ => { });
        var s = new PresetSession(store, new LookEditor(), "Natural", null)
        { ReShadeLink = new PresetReShadeLink(c.Current, c.Apply) };
        s.Apply(store.All.Single(p => p.Name == "Natural"));     // baseline: ReShade's own preset, on
        c.SetPresetPath(Folder + "/Noir.ini");
        s.OnReShadeEdited();
        Land(c);
        _fake.PresetCalls.Clear();
        s.ResetToSaved();
        Assert.Equal(new[] { Own }, _fake.PresetCalls);
    }

    // ── perf minors: settle reads, the cached IsOn delegate, Changed bookkeeping ──────────────────────────────────

    [Fact]
    public void The_on_off_settle_reads_the_value_refreshed_on_changed_not_the_native_getter()
    {
        var c = Make();
        c.SetEnabled(false);
        _fake.EnabledNow = false;                                 // native reads false, but no Changed yet
        c.Tick(Frame);
        Assert.True(c.Pending);
        c.OnChanged();
        c.Tick(Frame);
        Assert.False(c.Pending);
    }

    [Fact]
    public void IsOnFunc_is_one_cached_delegate_that_includes_wishes()
    {
        var c = Make();
        Assert.Same(c.IsOnFunc, c.IsOnFunc);
        c.SetTechnique(Mxao, true);
        Assert.True(c.IsOnFunc(Mxao));
    }

    [Fact]
    public void OnChanged_says_whether_the_current_preset_changed()
    {
        var c = Make();
        Assert.False(c.OnChanged());
        _fake.CurrentPreset = Folder + "/Noir.ini";
        Assert.True(c.OnChanged());
        Assert.False(c.OnChanged());
        _fake.EnabledNow = false;                                 // on/off alone is not a preset change
        Assert.False(c.OnChanged());
    }

    // ── perf minor: the pack that could not be queued because its requirement failed says which one ───────────────

    [Fact]
    public void A_dependent_of_a_failed_pack_names_the_failed_requirement()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var dl = new FakeDownloads();
        var i = new PackInstaller(dl, PackCatalog.All, _ => false, _ => { });
        i.Request(PackCatalog.SweetFx);
        dl.Calls[0].Done.SetResult(new DownloadResult(false, null, "checksum mismatch"));
        Assert.Equal(PackCatalog.Standard.Name, i.FailedRequirement(PackCatalog.SweetFx));
        Assert.Null(i.FailedRequirement(PackCatalog.Standard));   // failed on its own: its own error applies
        Assert.Equal("checksum mismatch", i.Error(PackCatalog.Standard));
        Assert.Null(i.FailedRequirement(PackCatalog.Prod80));
    }

    // ── qa minor: closing the studio drops a shot still waiting at the gate ───────────────────────────────────────

    [Fact]
    public void A_disarmed_gate_never_fires_and_reports_whether_it_was_armed()
    {
        var g = new CaptureGate();
        g.Arm(0);
        Assert.True(g.Disarm());
        Assert.False(g.Armed);
        Assert.False(g.Tick(false));
        Assert.False(g.Disarm());
    }

    // Fully qualified: Stellar.PhotoStudio.ReShade also declares an IPresetFiles (Task 5) — the two are unrelated
    // interfaces that happen to share a name, so the bare name is ambiguous now that both namespaces are imported here.
    private sealed class MemFiles : Stellar.PhotoStudio.Presets.IPresetFiles
    {
        private readonly Dictionary<string, string> _files = new();
        public IEnumerable<string> List() => _files.Keys.ToList();
        public string? Read(string n) => _files.TryGetValue(n, out var j) ? j : null;
        public void Write(string n, string j) => _files[n] = j;
        public void Delete(string n) => _files.Remove(n);
    }

    // qa re-review: a Look preset applied while Photo Studio is CLOSED (the next-preset hotkey) must not change ReShade
    // behind O1's back — it waits for the next open, where it is applied after the snapshot (so closing puts it back).
    [Fact]
    public void A_choice_applied_while_closed_waits_for_the_next_open()
    {
        var c = Make();
        c.OnStudioClosed();                                        // the plugin starts closed
        c.Apply(new ReShadeChoice(Folder + "/Noir.ini", false));
        Assert.Empty(_fake.PresetCalls);
        Assert.Empty(_fake.EnabledRequests);

        c.OnStudioOpened();
        Assert.Equal(new[] { Folder + "/Noir.ini" }, _fake.PresetCalls);
        Assert.Equal(new[] { false }, _fake.EnabledRequests);
        Land(c);
        _fake.PresetCalls.Clear();
        _fake.EnabledRequests.Clear();

        c.OnStudioClosed();
        Assert.Equal(new[] { Own }, _fake.PresetCalls);             // the snapshot was taken BEFORE the deferred choice
        Assert.Equal(new[] { true }, _fake.EnabledRequests);
    }

    // qa re-review: "edits are never lost" — the ReShade change made in Photo Studio comes back when it reopens (the
    // close put the game's own state back per O1; the session's state returns with the rest of the look).
    [Fact]
    public void Reopening_re_applies_the_reshade_state_the_studio_had_at_close()
    {
        var c = Make();
        c.OnStudioOpened();
        c.SetPresetPath(Folder + "/Noir.ini");
        Land(c);
        c.OnStudioClosed();
        Land(c);                                                   // back on the player's own preset
        _fake.PresetCalls.Clear();
        _fake.EnabledRequests.Clear();

        c.OnStudioOpened();
        Assert.Equal(new[] { Folder + "/Noir.ini" }, _fake.PresetCalls);
        Assert.Empty(_fake.EnabledRequests);                       // on/off did not change, so nothing re-sent
    }

    // qa re-review 2: closing DURING a ReShade reload must still restore — as soon as ReShade is Ready again.
    [Fact]
    public void A_close_during_a_reload_restores_once_reshade_is_ready()
    {
        var c = Make();
        c.OnStudioOpened();
        c.SetPresetPath(Folder + "/Noir.ini");
        Land(c);
        _fake.State = ReShadeState.Loading;
        _fake.PresetCalls.Clear();
        c.OnStudioClosed();
        Assert.Empty(_fake.PresetCalls);                          // nothing can be sent mid-reload

        _fake.State = ReShadeState.Ready;
        c.OnChanged();
        Assert.Equal(new[] { Own }, _fake.PresetCalls);           // restored as soon as it is Ready
        Land(c);
        _fake.PresetCalls.Clear();
        c.OnStudioOpened();
        Assert.Equal(new[] { Folder + "/Noir.ini" }, _fake.PresetCalls);   // and the edit comes back on reopen
    }

    // qa re-review 2: an on/off-only look applied while closed keeps the remembered edit's preset.
    [Fact]
    public void An_on_off_only_look_applied_while_closed_keeps_the_remembered_preset()
    {
        var c = Make();
        c.OnStudioOpened();
        c.SetPresetPath(Folder + "/Noir.ini");
        Land(c);
        c.OnStudioClosed();
        Land(c);
        c.Apply(new ReShadeChoice(null, false));                   // a look saved on ReShade's own preset: on/off only
        _fake.PresetCalls.Clear();
        _fake.EnabledRequests.Clear();
        c.OnStudioOpened();
        Assert.Equal(new[] { Folder + "/Noir.ini" }, _fake.PresetCalls);
        Assert.Equal(new[] { false }, _fake.EnabledRequests);
    }
}
