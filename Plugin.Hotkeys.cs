using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

public sealed partial class Plugin
{
    private readonly List<IHotkeyAction> _hotkeys = new();

    // Defaults live in StudioHotkeys (tested). nextpreset ships unbound; the player can bind it in Settings.
    private void DeclareHotkeys()
    {
        foreach (var (id, locKey, key) in StudioHotkeys.Defaults) Declare(id, locKey, key, CallbackFor(id));
    }

    private Action CallbackFor(string id) => id switch
    {
        StudioHotkeys.Capture => CaptureNow,
        StudioHotkeys.Panel => TogglePanel,
        StudioHotkeys.FreeCam => ToggleFreeCamera,
        StudioHotkeys.HideAll => ToggleHideAll,
        _ => NextPreset,
    };

    private void Declare(string id, string locKey, KeyBinding? key, Action cb) =>
        _hotkeys.Add(_services.Hotkeys.DeclareAction(
            new HotkeyAction(Id: id, Description: _loc.T(locKey), SuggestedDefault: key), cb));

    private bool _hideAllNoticeDone;

    // Spec D7: once per install, before DeclareHotkeys, so the hide-all declare already sees the migrated binding.
    private void RunHideAllMigration() =>
        HideAllMigration.Run(_services.Hotkeys, _services.Config.GetSection("photostudio"));

    // The one-time toast, on the first frame in the world after the update (a toast before the world is never seen).
    private void TickHideAllNotice()
    {
        if (_hideAllNoticeDone || !InWorld()) return;
        _hideAllNoticeDone = true;
        var cfg = _services.Config.GetSection("photostudio");
        var notice = HideAllMigration.Pending(cfg);
        if (notice == HideAllNotice.None) return;
        _services.Notifications.Notify(
            T(notice == HideAllNotice.Moved ? "fc.toast.hideallMoved" : "fc.toast.hideallCleared"), NotificationKind.Info);
        HideAllMigration.MarkShown(cfg);
    }
}
