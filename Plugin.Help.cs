using System;
using System.Collections.Generic;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// "?" help popovers and the shared row widgets — the StellarMaestroPlugin recipe (Plugin.Help.cs): the window
// framework has no hover tooltip, so a "?" button opens one shared floating tip window anchored under it.
public sealed partial class Plugin
{
    private readonly Dictionary<string, (Func<string> Title, Func<string> Body)> _helpFns = new();
    private IWindowControl _tipWindow = null!;
    private string _tipKey = "";
    private const float TipWidth = 320f;
    private WindowRect _tipRect;
    private int _tipRepositionTicks;

    private const float LabelW = 96f;
    private const float ValueW = 76f;   // 76: "20.0 m/dtk" (id, max speed) measured 74 px of ink (sandbox S8)

    // TextMuted (not MenuMuted): MenuMuted is darker in every preset and failed the three dark themes (sandbox re-measure).
    // Light-theme muted contrast (1.82:1) is a theme-token issue, not fixed here.
    private ColorRgba? Muted() => _services.Theme.Colors.TextMuted;
    private ColorRgba? MenuMuted() => _services.Theme.Colors.MenuMuted;
    // Explicit default text colour: a Color func that flips back to null does not restore the chrome default
    // (measured in-game: a pill kept its warning colour after unpinning), so dynamic colours never return null.
    private ColorRgba? Normal() => _services.Theme.Colors.MenuText;
    private string T(string key) => _loc.T(key);

    private void ToggleTip(string key, WindowRect r)
    {
        if (_tipKey == key && _tipWindow.IsShown) { _tipKey = ""; _tipWindow.SetVisible(false); return; }
        _tipKey = key;
        // Open to the LEFT of the "?" (the panel sits at the screen's right edge), clamped on screen.
        float x = Math.Max(10f, r.X + r.Width - TipWidth);
        float y = Math.Min(r.Y + r.Height + 4f, _services.Framework.ScreenHeight - 40f);
        _tipRect = new WindowRect(x, y, TipWidth, 0f);
        _tipWindow.SetVisible(true);
        _tipWindow.SetRect(_tipRect);
        _tipWindow.BringToFront();
        _tipWindow.MarkDirty();
        _tipRepositionTicks = 4;   // the first mount applies DefaultRect after SetRect — re-assert for a few frames
    }

    /// <summary>Closes the open "?" popover; true when one was open (Esc in the free camera closes it first).</summary>
    private bool DismissHelpTip()
    {
        if (_tipWindow is not { IsShown: true }) return false;
        _tipKey = "";
        _tipWindow.SetVisible(false);
        return true;
    }

    private void TipRepositionTick()
    {
        if (_tipRepositionTicks <= 0) return;
        _tipRepositionTicks--;
        if (_tipWindow.IsShown) _tipWindow.SetRect(_tipRect);
    }

    private IWindowControl RegisterTipWindow()
    {
        IWindowControl w = null!;
        w = _services.Windows.Register(new WindowRegistration(
            Spec: new WindowSpec(
                Id: "photostudio.tip",
                Title: T("ps.win.help"),
                DefaultRect: new WindowRect(_services.Framework.ScreenWidth - TipWidth - 20f, 20f, TipWidth, 0f),
                Category: WindowCategory.Tools,
                Style: WindowPanelStyle.GlassMenu)
            { Draggable = true, Closable = true, StartVisible = false, ShouldRender = InWorld },
            Root: new ColumnElement(new HudElement[]
            {
                new TextElement(() => _helpFns.TryGetValue(_tipKey, out var e) ? e.Title() : "", Emphasis: true),
                new TextElement(() => _helpFns.TryGetValue(_tipKey, out var e) ? e.Body() : "", Color: Muted),
            }, Gap: 6f),
            OnClose: () => { _tipKey = ""; w!.SetVisible(false); }));
        return w;
    }

    private HudElement HelpDot(string key, Func<string> title, Func<string> body)
    {
        _helpFns[key] = (title, body);
        return new CellElement(new ButtonElement(
            Label: () => "?",
            OnClick: () => { },
            Active: () => _tipKey == key)
        { OnClickWithRect = r => ToggleTip(key, r) }, Width: 26f);
    }

    /// <summary>A toggle's label plus the body of its "?" popover.</summary>
    private readonly record struct HelpText(Func<string> Label, Func<string> Body);

    /// <summary>[toggle] [label] … [?] — the "?" lands in the panel's right-edge column.</summary>
    private HudElement HelpToggle(string key, Func<bool> get, Action<bool> set, HelpText text, Func<bool>? enabled = null)
        => new RowElement(new HudElement[]
        {
            new ToggleElement(Label: () => "", Get: get, Set: set, Enabled: enabled),
            new TextElement(text.Label, Color: () => enabled is null || enabled() ? Normal() : MenuMuted()),
            new SpacerElement(),
            HelpDot(key, text.Label, text.Body),
        }, Gap: 6f);

    /// <summary>[label] [slider] [value] [↺] — Maestro's slider row without the "?" (the group header carries it).</summary>
    private HudElement SliderRow(Func<string> label, SliderElement slider, Func<string> value, Action reset)
        => new RowElement(new HudElement[]
        {
            new CellElement(new TextElement(label, Color: Muted), Width: LabelW),
            new CellElement(slider with { SquareHandle = true }, Weight: 1f),
            new CellElement(new TextElement(value, Align: TextAlign.Right), Width: ValueW),
            new CellElement(new ButtonElement(Label: () => "↺", OnClick: reset), Width: 28f),
        }, Gap: 6f);

    /// <summary>[label] [control…] — a fixed-width muted label followed by the given controls.</summary>
    private HudElement LabeledRow(Func<string> label, params HudElement[] controls)
    {
        var children = new List<HudElement>(controls.Length + 1)
        {
            new CellElement(new TextElement(label, Color: Muted), Width: LabelW),
        };
        children.AddRange(controls);
        return new RowElement(children, Gap: 6f);
    }

    private static string F(float v, string format) => v.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
}
