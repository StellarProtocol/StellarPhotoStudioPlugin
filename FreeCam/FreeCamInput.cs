using System.Collections.Generic;
using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>One-shot actions this frame (press edges), a left click's pointer, and Esc (exit).</summary>
internal readonly record struct FreeCamEdges(
    bool ToggleMode, bool ToggleFreeze, bool Reset, bool ToggleHint, bool BackToSelf, (float X, float Y)? Click, bool Exit)
{
    /// <summary>L (lights spec § 2): drop a lamp at the camera; with Shift, move the selected lamp there.</summary>
    public bool DropLamp { get; init; }
    public bool MoveLamp { get; init; }
    /// <summary>U (game UI spec 2026-10-10): show or hide the game's interface without leaving the free camera.</summary>
    public bool ToggleGameUi { get; init; }
}

/// <summary>Maps the shield handle's raw input to a <see cref="CamIntent"/> plus press edges (spec § 3 key map:
/// WASD/QE move, RMB look, wheel zoom, Shift+wheel FOV, Z/C roll, Shift fast, Ctrl slow, Tab mode, Space freeze,
/// R reset, H hint, U game UI, Backspace back to you, left click pick, Esc exit — spec D8). While a Stellar text field has focus
/// (<see cref="IInputShieldHandle.TextFieldFocused"/>) the keys and the wheel belong to that field: movement, roll,
/// wheel and every key edge (Esc included) read as nothing; mouse-look and the left-click pick carry on.</summary>
internal sealed class FreeCamInput
{
    private static readonly StellarKeyCode[] EdgeKeys =
        { StellarKeyCode.Tab, StellarKeyCode.Space, StellarKeyCode.R, StellarKeyCode.H, StellarKeyCode.Backspace, StellarKeyCode.Escape, StellarKeyCode.L, StellarKeyCode.U };

    private readonly HashSet<StellarKeyCode> _down = new();
    private bool _leftDown;

    public (CamIntent Intent, FreeCamEdges Edges) Read(IInputShieldHandle h)
    {
        var mods = h.Modifiers;
        var (dx, dy) = h.MouseDelta;
        var intent = new CamIntent
        {
            Move = new Vector3(Axis(h, StellarKeyCode.D, StellarKeyCode.A), Axis(h, StellarKeyCode.E, StellarKeyCode.Q),
                               Axis(h, StellarKeyCode.W, StellarKeyCode.S)),
            LookX = dx,
            LookY = dy,
            Looking = h.IsMouseHeld(1),
            Wheel = h.Wheel,
            RollAxis = Axis(h, StellarKeyCode.C, StellarKeyCode.Z),
            Shift = (mods & ModifierKeys.Shift) != 0,
            Ctrl = (mods & ModifierKeys.Ctrl) != 0,
        };
        var alt = (mods & ModifierKeys.Alt) != 0;
        // Every Edge() runs even while typing, so a key still held when the field loses focus never fires late.
        var tab = Edge(h, StellarKeyCode.Tab);
        var lamp = Edge(h, StellarKeyCode.L);
        var edges = new FreeCamEdges(tab && !alt, Edge(h, StellarKeyCode.Space), Edge(h, StellarKeyCode.R),
            Edge(h, StellarKeyCode.H), Edge(h, StellarKeyCode.Backspace), Click(h), Edge(h, StellarKeyCode.Escape))
        {
            DropLamp = lamp && !intent.Shift,
            MoveLamp = lamp && intent.Shift,
            ToggleGameUi = Edge(h, StellarKeyCode.U),
        };
        if (!h.TextFieldFocused) return (intent, edges);
        return (intent with { Move = Vector3.Zero, Wheel = 0f, RollAxis = 0f },
                new FreeCamEdges(false, false, false, false, false, edges.Click, false));
    }

    /// <summary>Marks keys already held at entry as "down" so they do not fire on the first frame.</summary>
    public void Prime(IInputShieldHandle h)
    {
        _down.Clear();
        foreach (var k in EdgeKeys) if (h.IsHeld(k)) _down.Add(k);
        _leftDown = h.IsMouseHeld(0);
    }

    private bool Edge(IInputShieldHandle h, StellarKeyCode key)
    {
        if (h.IsHeld(key)) return _down.Add(key);
        _down.Remove(key);
        return false;
    }

    private (float X, float Y)? Click(IInputShieldHandle h)
    {
        var held = h.IsMouseHeld(0);
        var edge = held && !_leftDown;
        _leftDown = held;
        return edge ? h.Pointer : null;
    }

    private static float Axis(IInputShieldHandle h, StellarKeyCode plus, StellarKeyCode minus) =>
        (h.IsHeld(plus) ? 1f : 0f) - (h.IsHeld(minus) ? 1f : 0f);
}
