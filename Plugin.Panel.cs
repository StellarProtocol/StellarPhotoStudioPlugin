using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

// The full panel, 2.0.0 house-style redesign (approved prototype _scratch\prototypes\photostudio-redesign-APPROVED.html,
// option A "rail"): a GlassMenu 600 px wide whose height resizes 560–1000, laid out as
//   ToolHeader [icon · Photo Studio / live status · Keep look chip · ?]
//   (the close / delete confirms are the framework modal over all of it; Keys and More are in-window popovers)
//   [64 px icon rail: Capture · Hide · Camera · Pose · Lights · Look · ReShade] [the section, scrolling inside, FILLS the height]
//   footer [Capture pill · one status line · Keys]
// A NEW window id ("photostudio.studio"): the 1.x "photostudio.panel" saved rect was 440 px wide, so it must not apply.
// Each section is its own Plugin.Section.<Name>.cs; the status texts and their caches are Plugin.Panel.Status.cs.
public sealed partial class Plugin
{
    private const string PanelWindowId = "photostudio.studio";
    private const float PanelW = 600f, PanelMinH = 560f, PanelMaxH = 1000f, PanelDefaultH = 720f;
    // The scroll viewport's preferred height: well under the 560 minimum, because the section area takes all the slack.
    private const float SectionMinH = 200f;
    private const float RailPad = 4f;
    private const float FooterCaptureLabelW = 126f;   // 150 px pill (prototype), "Capturing..." in every locale
    private const float FooterKeysLabelW = 44f;
    private const float ConfirmPillLabelW = 80f;      // "Close" / "Delete" in every locale (the modal confirm pill)

    private IWindowControl RegisterPanelWindow()
    {
        var win = _services.Windows.Register(new WindowRegistration(
            new WindowSpec(
                Id: PanelWindowId,
                Title: T("ps.title"),
                DefaultRect: new WindowRect(-20f, 140f, PanelW, PanelDefaultH),
                Category: WindowCategory.Tools,
                Style: WindowPanelStyle.GlassMenu)
            {
                StartVisible = false, Closable = true, Draggable = true, Resizable = true,
                Anchor = WindowAnchor.TopRight,
                MinWidth = PanelW, MaxWidth = PanelW, MinHeight = PanelMinH, MaxHeight = PanelMaxH,
                ShouldRender = InWorld,
                TitleProvider = () => _loc.T("ps.title"),
                // A big window opened by hand: build it at the login screen / behind the loading screen, not in the
                // frame the player presses Shift+F10 (Plugin-Performance-Rules §5). Its own canvas: the section scroll
                // then never rebatches the game HUD's other windows.
                Prebuild = true,
                IsolateCanvas = true,
            },
            BuildPanelRoot(),
            TitleTrailing: new RowElement(new HudElement[]
            {
                new ButtonElement(() => "–", OnClick: MinimizePanel, Width: 24f),   // Plugin.Close.cs — to the compact strip
            }, Gap: 6f),
            OnClose: RequestClose));   // the chrome ✕ = Close Photo Studio (asks first when a scene is running)
        // Width locked to 600, height free between 560 and 1000 (fw 2.33 SetSizeLimits; the spec limits say the same).
        win.SetSizeLimits(PanelW, PanelMinH, PanelW, PanelMaxH);
        return win;
    }

    // The confirm host covers the whole content (the modal locks the header, rail and footer too); the popovers sit
    // inside it, so an open confirm also covers an open popover.
    private HudElement BuildPanelRoot() => new ConfirmHostElement(WithPopovers(new ColumnElement(new HudElement[]
    {
        PanelHeader(),
        // Fill: the rail + section row takes every spare pixel of the window's height, so a taller window shows more
        // of the section instead of empty space under the footer (1.x was stuck at a 420 px tab area).
        new ConditionalElement(static () => true, new RowElement(new HudElement[] { Rail(), SectionArea() }, Gap: 8f), Fill: true),
        PanelFooter(),
    }, Gap: 8f)), _panelConfirm) { ConfirmLabelWidth = ConfirmPillLabelW };

    // ── header ────────────────────────────────────────────────────────────────────────────────────────────────────
    // "Keep look" + its chip replace both the 1.x "Keep look while playing" toggle and the "Look pinned" title-bar pill
    // (same setting, look.pinned).
    private HudElement PanelHeader() => ToolHeader("header", "ps.title",
        StatusText(() => _hdrStatus, () => _hdrStatusAccent ? AccentColor() : MutedColor()),
        new HudElement[]
        {
            new TextElement(Loc("ps.hdr.keepLook"), Color: () => MutedColor(), NoWrap: true, FontSize: 12) { Bold = true },
            OnOffChip(() => _settings.Pinned, SetPinned),
            HelpButton(new HelpKeys("ps.look.pin", "ps.help.look.pin", "ps.title")),
        });

    // ── icon rail ─────────────────────────────────────────────────────────────────────────────────────────────────
    // A panel-coloured column of RailItems. The Fill Conditional makes it as tall as the section beside it (prototype
    // .main: the rail stretches, its items stay at the top).
    private HudElement Rail() => new ConditionalElement(static () => true, new BubbleElement(new ColumnElement(new HudElement[]
    {
        RailEntry(StudioSections.Capture, "rail_capture", "ps.tab.capture"),
        RailEntry(StudioSections.Hide, "rail_hide", "ps.sec.hide"),
        RailEntry(StudioSections.Camera, "rail_camera", "ps.tab.camera"),
        RailEntry(StudioSections.Pose, "rail_pose", "ps.sec.pose"),
        RailEntry(StudioSections.Lights, "rail_lights", "ps.tab.lights"),
        RailEntry(StudioSections.Look, "rail_look", "ps.tab.look"),
        RailEntry(StudioSections.ReShade, "rail_reshade", "ps.sec.reshade"),
    }, Gap: 4f))
    {
        FillColorFn = () => Fade(PanelColor()), CornerRadius = TileRadius, PaddingX = RailPad, PaddingY = RailPad,
    }, Fill: true);

    private HudElement RailEntry(int section, string icon, string labelKey)
        => RailItem(icon, labelKey, () => _settings.Section == section, () => SelectSection(section));

    private void SelectSection(int section)
    {
        if (_settings.Section == section) return;
        _settings.SetSection(section);
        CloseHelpPopover();   // its "?" just left the screen
        _panelWin.MarkDirty();
    }

    // ── sections ──────────────────────────────────────────────────────────────────────────────────────────────────
    // One scroll per section (each keeps its own scroll position), each in a Fill Conditional so the visible one takes
    // the full height. Prebuild: the hidden sections build in the background after the window mounts, so the first
    // click on a rail item is a re-show, not a build.
    private HudElement SectionArea() => new CellElement(new ColumnElement(new HudElement[]
    {
        SectionScroll(StudioSections.Capture, BuildCaptureSection()),     // Plugin.Section.Capture.cs
        SectionScroll(StudioSections.Hide, BuildHideSection()),           // Plugin.Section.Hide.cs
        SectionScroll(StudioSections.Camera, BuildCameraSection()),       // Plugin.Section.Camera.cs
        SectionScroll(StudioSections.Pose, BuildPoseSection()),           // Plugin.Section.Pose.cs
        SectionScroll(StudioSections.Lights, BuildLightsSection()),       // Plugin.Section.Lights.cs
        SectionScroll(StudioSections.Look, BuildLookSection()),           // Plugin.Section.Look.cs
        SectionScroll(StudioSections.ReShade, BuildReShadeSection()),     // Plugin.Section.ReShade.cs
    }, Gap: 0.01f), Weight: 1f);

    private HudElement SectionScroll(int section, HudElement body) => new ConditionalElement(() => _settings.Section == section,
        new ScrollElement(body, SectionMinH), Fill: true) { Prebuild = true };

    /// <summary>A section's panels, 8 px apart (prototype .content gap).</summary>
    private static HudElement SectionColumn(params HudElement[] panels) => new ColumnElement(panels, Gap: 8f);

    // ── footer ────────────────────────────────────────────────────────────────────────────────────────────────────
    // [Capture 150] [one status line: "2× · PNG · 3840 × 2160 · ReShade on"] … [Keys]. Keys opens the in-window Keys
    // popover (Plugin.Panel.Popovers.cs: every hotkey with its current binding).
    private HudElement PanelFooter() => new RowElement(new HudElement[]
    {
        Pill(PillKind.Primary, () => Capturing ? T("ps.capturing") : T("ps.capture"), CaptureNow, FooterCaptureLabelW,
            enabled: () => !Capturing, surface: WindowBg),
        new CellElement(new RowElement(new HudElement[]
        {
            new TextElement(StatusLine, Color: () => TextColor(), NoWrap: true) { Ellipsize = true },
            new ConditionalElement(() => _services.ReShade.State == ReShadeState.Ready && _rs.Enabled,
                new TextElement(ReShadeOnSuffix, Color: () => MutedColor(), NoWrap: true)),
        }, Gap: 0.01f), Weight: 1f),
        Pill(PillKind.Neutral, "ps.foot.keys", ToggleKeys, FooterKeysLabelW, surface: WindowBg),   // Plugin.Panel.Popovers.cs
    }, Gap: 10f);

}
