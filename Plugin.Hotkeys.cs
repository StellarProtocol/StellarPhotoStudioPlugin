using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

public sealed partial class Plugin
{
    private readonly List<IHotkeyAction> _hotkeys = new();

    // photostudio.capture = F10, photostudio.panel = Shift+F10, photostudio.hideall = Alt+F10 — three F10 chords
    // that never collide because the framework matches on the FULL (key, modifiers) pair. nextpreset ships unbound
    // (no default chord free on F10-4); the player can bind it in Settings.
    private void DeclareHotkeys()
    {
        Declare("photostudio.capture", "hotkey.capture", new KeyBinding(StellarKeyCode.F10), CaptureNow);
        Declare("photostudio.panel", "hotkey.panel", new KeyBinding(StellarKeyCode.F10, ModifierKeys.Shift), TogglePanel);
        Declare("photostudio.hideall", "hotkey.hideall", new KeyBinding(StellarKeyCode.F10, ModifierKeys.Alt), ToggleHideAll);
        Declare("photostudio.nextpreset", "hotkey.nextpreset", null, NextPreset);
    }

    private void Declare(string id, string locKey, KeyBinding? key, Action cb) =>
        _hotkeys.Add(_services.Hotkeys.DeclareAction(
            new HotkeyAction(Id: id, Description: _loc.T(locKey), SuggestedDefault: key), cb));
}
