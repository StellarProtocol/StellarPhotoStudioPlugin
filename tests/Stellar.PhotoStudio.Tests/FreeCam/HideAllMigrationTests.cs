using System.Collections.Generic;
using System.Linq;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Spec D7 (owner 2026-10-01): hide-all moves Alt+F10 → Ctrl+F10 once, the toast shows once, Alt+F10 is never bound twice.
public sealed class HideAllMigrationTests
{
    private static readonly KeyBinding AltF10 = new(StellarKeyCode.F10, ModifierKeys.Alt);
    private static readonly KeyBinding CtrlF10 = new(StellarKeyCode.F10, ModifierKeys.Ctrl);

    private sealed class FakeHotkeys : IHotkeys
    {
        public SavedBindingMigration Result = SavedBindingMigration.NothingSaved;
        public readonly List<(string Id, KeyBinding From, KeyBinding To)> Calls = new();
        public IHotkeyAction DeclareAction(HotkeyAction action, System.Action callback) => throw new System.NotSupportedException();
        public SavedBindingMigration MigrateSavedBinding(string actionId, KeyBinding from, KeyBinding to)
        {
            Calls.Add((actionId, from, to));
            return Result;
        }
    }

    [Fact]
    public void A_saved_Alt_F10_is_moved_to_Ctrl_F10_and_the_toast_is_owed()
    {
        var hk = new FakeHotkeys { Result = SavedBindingMigration.Moved };
        var cfg = new MemConfigSection();
        HideAllMigration.Run(hk, cfg);
        Assert.Equal(("photostudio.hideall", AltF10, CtrlF10), hk.Calls.Single());
        Assert.Equal(HideAllNotice.Moved, HideAllMigration.Pending(cfg));
    }

    [Fact]
    public void A_taken_Ctrl_F10_owes_the_unbound_toast()
    {
        var hk = new FakeHotkeys { Result = SavedBindingMigration.Cleared };
        var cfg = new MemConfigSection();
        HideAllMigration.Run(hk, cfg);
        Assert.Equal(HideAllNotice.Cleared, HideAllMigration.Pending(cfg));
    }

    [Fact]
    public void A_1_0_player_on_the_default_gets_the_toast_without_a_saved_binding()
    {
        var cfg = new MemConfigSection();
        cfg.Values["ui.tab"] = 1;                         // written by 1.0.0's StudioSettings
        HideAllMigration.Run(new FakeHotkeys(), cfg);
        Assert.Equal(HideAllNotice.Moved, HideAllMigration.Pending(cfg));
    }

    [Fact]
    public void A_new_install_gets_no_toast()
    {
        var cfg = new MemConfigSection();
        HideAllMigration.Run(new FakeHotkeys(), cfg);
        Assert.Equal(HideAllNotice.None, HideAllMigration.Pending(cfg));
        Assert.True(cfg.Get(HideAllMigration.DoneKey, false));
    }

    [Fact]
    public void A_hide_all_the_player_moved_elsewhere_gets_no_toast()
    {
        var cfg = new MemConfigSection();
        cfg.Values["ui.tab"] = 1;
        HideAllMigration.Run(new FakeHotkeys { Result = SavedBindingMigration.KeptOther }, cfg);
        Assert.Equal(HideAllNotice.None, HideAllMigration.Pending(cfg));
    }

    [Fact]
    public void It_runs_once_per_install()
    {
        var hk = new FakeHotkeys { Result = SavedBindingMigration.Moved };
        var cfg = new MemConfigSection();
        HideAllMigration.Run(hk, cfg);
        hk.Result = SavedBindingMigration.Cleared;
        HideAllMigration.Run(hk, cfg);
        Assert.Single(hk.Calls);
        Assert.Equal(HideAllNotice.Moved, HideAllMigration.Pending(cfg));
    }

    [Fact]
    public void The_toast_stays_owed_until_shown_once()
    {
        var cfg = new MemConfigSection();
        HideAllMigration.Run(new FakeHotkeys { Result = SavedBindingMigration.Moved }, cfg);
        Assert.Equal(HideAllNotice.Moved, HideAllMigration.Pending(cfg));   // e.g. the game closed before the world
        HideAllMigration.MarkShown(cfg);
        Assert.Equal(HideAllNotice.None, HideAllMigration.Pending(cfg));
        HideAllMigration.Run(new FakeHotkeys { Result = SavedBindingMigration.Moved }, cfg);
        Assert.Equal(HideAllNotice.None, HideAllMigration.Pending(cfg));
    }
}
