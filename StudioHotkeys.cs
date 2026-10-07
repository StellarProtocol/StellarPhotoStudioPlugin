using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

/// <summary>Photo Studio's hotkey ids and default chords. The framework matches the FULL (key, modifiers) pair, so the
/// F10 chords never collide. hideall moved from Alt+F10 (1.0.0) to Ctrl+F10 so Alt+F10 can be the free camera (spec § 3).</summary>
internal static class StudioHotkeys
{
    public const string Capture = "photostudio.capture";
    public const string Panel = "photostudio.panel";
    public const string FreeCam = "photostudio.freecam";
    public const string HideAll = "photostudio.hideall";
    public const string NextPreset = "photostudio.nextpreset";
    public const string Close = "photostudio.close";
    // 1.7.0 (player request "FOV Slider"): free camera only; in/out repeat while held (FovKeys).
    public const string FovIn = "photostudio.fovin";
    public const string FovOut = "photostudio.fovout";
    public const string FovReset = "photostudio.fovreset";

    public static readonly (string Id, string LocKey, KeyBinding? Key)[] Defaults =
    {
        (Capture, "hotkey.capture", new KeyBinding(StellarKeyCode.F10)),
        (Panel, "hotkey.panel", new KeyBinding(StellarKeyCode.F10, ModifierKeys.Shift)),
        (FreeCam, "hotkey.freecam", new KeyBinding(StellarKeyCode.F10, ModifierKeys.Alt)),
        (HideAll, "hotkey.hideall", new KeyBinding(StellarKeyCode.F10, ModifierKeys.Ctrl)),
        (NextPreset, "hotkey.nextpreset", null),
        (Close, "hotkey.close", new KeyBinding(StellarKeyCode.F10, ModifierKeys.Ctrl | ModifierKeys.Shift)),
        (FovIn, "hotkey.fovin", new KeyBinding(StellarKeyCode.RightBracket)),
        (FovOut, "hotkey.fovout", new KeyBinding(StellarKeyCode.LeftBracket)),
        (FovReset, "hotkey.fovreset", new KeyBinding(StellarKeyCode.Backslash)),
    };
}
