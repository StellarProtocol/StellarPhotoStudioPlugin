using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>The one-time toast owed after the hide-all move (spec D7).</summary>
internal enum HideAllNotice { None = 0, Moved = 1, Cleared = 2 }

/// <summary>
/// Spec D7 (owner 2026-10-01): Photo Studio 1.0.0 had "Hide everything" on Alt+F10; 1.1.0 gives Alt+F10 to the free
/// camera and hide-all to Ctrl+F10. Runs once per install, BEFORE the hotkeys are declared: a SAVED Alt+F10 is moved by
/// <see cref="IHotkeys.MigrateSavedBinding"/> (unbound instead when Ctrl+F10 is taken). A 1.0.0 player on the default
/// needs no move (the framework never saves defaults) but still gets the toast. The toast stays owed until shown once.
/// </summary>
internal static class HideAllMigration
{
    internal const string DoneKey = "migrations.hideall.ctrlF10";
    internal const string NoticeKey = "migrations.hideall.notice";
    internal static readonly KeyBinding OldChord = new(StellarKeyCode.F10, ModifierKeys.Alt);
    internal static readonly KeyBinding NewChord = new(StellarKeyCode.F10, ModifierKeys.Ctrl);

    public static void Run(IHotkeys hotkeys, IConfigSection cfg)
    {
        if (cfg.Get(DoneKey, false)) return;
        var notice = hotkeys.MigrateSavedBinding(StudioHotkeys.HideAll, OldChord, NewChord) switch
        {
            SavedBindingMigration.Moved => HideAllNotice.Moved,
            SavedBindingMigration.Cleared => HideAllNotice.Cleared,
            SavedBindingMigration.NothingSaved when UsedVersion1(cfg) => HideAllNotice.Moved,
            _ => HideAllNotice.None,
        };
        cfg.Set(DoneKey, true);
        cfg.Set(NoticeKey, (int)notice);
        cfg.SaveQuiet();
    }

    public static HideAllNotice Pending(IConfigSection cfg) => (HideAllNotice)cfg.Get(NoticeKey, 0);

    public static void MarkShown(IConfigSection cfg)
    {
        cfg.Set(NoticeKey, (int)HideAllNotice.None);
        cfg.SaveQuiet();
    }

    /// <summary>True when the plugin config holds a key 1.0.0 writes on its first settings change. A 1.0.0 player who
    /// never changed a setting is indistinguishable from a new install (Open question 18).</summary>
    internal static bool UsedVersion1(IConfigSection cfg) =>
        cfg.Get("ui.tab", -1) >= 0 || cfg.Get("capture.scale", -1) >= 0 || cfg.Get("hide.layers", -1) >= 0 ||
        cfg.Get("ui.openGroups", -1) >= 0 || cfg.Get<string?>("look.presetName", null) is not null ||
        cfg.Get<string?>("capture.format", null) is not null;
}
