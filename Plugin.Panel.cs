using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// The full panel (approved mockup, docs/superpowers/specs/assets/2026-09-30-photo-studio-panel-layout.md § 2):
// GlassMenu 440×660 at the top-right, four tabs (Capture · Look · Camera · Presets) (CooldownBar tab-strip recipe), each tab in its own scroll
// area, and a fixed Capture footer on every tab so the player can tune a look and shoot without switching.
public sealed partial class Plugin
{
    private IWindowControl RegisterPanelWindow() => _services.Windows.Register(new WindowRegistration(
        new WindowSpec(
            Id: "photostudio.panel",
            Title: T("ps.title"),
            DefaultRect: new WindowRect(-20f, 140f, 440f, 660f),
            Category: WindowCategory.Tools,
            Style: WindowPanelStyle.GlassMenu)
        {
            StartVisible = false, Closable = true, Draggable = true, Resizable = true,
            Anchor = WindowAnchor.TopRight,
            MinWidth = 400f, MinHeight = 420f, MaxWidth = 640f, MaxHeight = 1000f,
            ShouldRender = InWorld,
        },
        BuildPanelRoot(),
        TitleTrailing: new ConditionalElement(() => _settings.Pinned,
            new PillElement(() => T("ps.pill.pinned"), Color: () => _services.Theme.Colors.Accent)),
        OnClose: () => _panel.Set(false)));

    private HudElement BuildPanelRoot() => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            TabButton(StudioTabs.Capture, "ps.tab.capture"),
            TabButton(StudioTabs.Look, "ps.tab.look"),
            TabButton(StudioTabs.Camera, "ps.tab.camera"),
            TabButton(StudioTabs.Lights, "ps.tab.lights"),
            TabButton(StudioTabs.Presets, "ps.tab.presets"),
        }, Gap: 4f),
        new SeparatorElement(),
        new ConditionalElement(() => _settings.Tab == StudioTabs.Capture, new ScrollElement(BuildCaptureTab(), Height: 420f), Fill: true),
        new ConditionalElement(() => _settings.Tab == StudioTabs.Look, new ScrollElement(BuildLookTab(), Height: 420f), Fill: true),
        new ConditionalElement(() => _settings.Tab == StudioTabs.Camera, new ScrollElement(BuildCameraTab(), Height: 420f), Fill: true),
        new ConditionalElement(() => _settings.Tab == StudioTabs.Lights, new ScrollElement(BuildLightsTab(), Height: 420f), Fill: true),
        new ConditionalElement(() => _settings.Tab == StudioTabs.Presets, new ScrollElement(BuildPresetsTab(), Height: 420f), Fill: true),
        new SeparatorElement(),
        BuildCaptureFooter(),
    }, Gap: 8f);

    private HudElement TabButton(int tab, string key) => new CellElement(
        new ButtonElement(() => T(key), OnClick: () => _settings.SetTab(tab), Active: () => _settings.Tab == tab),
        Weight: 1f);

    private HudElement BuildCaptureFooter() => new ColumnElement(new HudElement[]
    {
        new CellElement(new ButtonElement(
            () => Capturing ? T("ps.capturing") : T("ps.capture"),
            OnClick: CaptureNow,
            Enabled: () => !Capturing,
            Style: MenuButtonStyle.Filled), Weight: 1f),
        new TextElement(StatusLine, Color: Muted, Align: TextAlign.Center, NoWrap: true),
        // Mockup "· ReShade on" — its own short line: appended to the status it overflowed the window in fil (ux-ui review).
        new ConditionalElement(() => _services.ReShade.State == ReShadeState.Ready && _rs.Enabled,
            new TextElement(() => T("ps.rs.status.on"), Color: () => _services.Theme.Colors.Accent, Align: TextAlign.Center, NoWrap: true)),
        new TextElement(HotkeyHint, Color: Muted, Align: TextAlign.Center, NoWrap: true),
    }, Gap: 4f);

    private string StatusLine()
    {
        if (_captureGate.Armed && _rs.Pending) return T("ps.rs.status.applying");   // R1: waiting for a ReShade switch
        if (Capturing)
            return _settings.Scale == 4 ? T("ps.status.capturing4x") : T("ps.capturing");
        var scale = EffectiveScale();
        if (_settings.Shape != PhotoShape.Screen)   // a shape keeps its scale and shrinks both sides; the size is real
            return ShapeFrame.StatusText(_settings.Scale, FormatName(), PlannedSize(), _settings.Shape);
        return scale < _settings.Scale
            ? _loc.TFormat("ps.status.capped", _settings.Scale, scale, FormatName(), ResolutionText())
            : $"{scale}× · {FormatName()} · {ResolutionText()}";
    }

    private string FormatName() => _settings.Format == CaptureFormat.Jpg ? "JPG" : "PNG";

    // The REAL output size (framework plan: scale caps, shape and GPU limit) — spec 2026-10-03 § 4.
    private string ResolutionText() => ShapeFrame.SizeText(PlannedSize());

    private string HotkeyHint() => _loc.TFormat("ps.hint.hotkeys",
        BindingText("photostudio.capture"), BindingText("photostudio.panel"), BindingText("photostudio.hideall"));

    private string BindingText(string id)
    {
        foreach (var h in _hotkeys)
            if (h.Id == id) return h.CurrentBinding?.ToString() ?? "—";
        return "—";
    }
}
