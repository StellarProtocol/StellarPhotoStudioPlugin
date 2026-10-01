using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Posing;

namespace Stellar.PhotoStudio;

// Person group (spec 2026-10-02 § 3; approved mockup 2026-10-01-free-camera-mockup.html, "Person"): ‹ name kind ›, the
// select hint, the copy note; Pose (picker ▾ + ▶/❚❚ + ↺, Moment, cloth hint); Expression (‹ name › + Hold); Head and
// Eyes (Default | Lens | Free + Lock, arrow pad in Free); Rotate; Reset this person. Theme colours only. Every control
// is a lambda read when it fires — the tree is built once in RegisterWindows.
public sealed partial class Plugin
{
    private const float PersonArrowW = 30f, LockW = 90f, PadW = 30f;
    private const int SubFont = 11;
    private bool _poseListOpen;

    private ColorRgba? Accent() => _services.Theme.Colors.MenuAccent;

    private HudElement PersonGroup() => FoldGroup("pz.group.person", "pz.help.person",
        () => _fcSettings.PoseOpen, open => _fcSettings.SetPoseOpen(open), new HudElement[]
        {
            new ConditionalElement(() => _freeCam.Active, PersonBody(), new TextElement(() => T("pz.off"), Color: Muted)),
        });

    private HudElement PersonBody() => new ColumnElement(new HudElement[]
    {
        PersonRow(),
        new TextElement(() => T("pz.hint.select"), Color: Muted, FontSize: SubFont),
        new ConditionalElement(() => _posingCtl.IsCopy,
            new PanelElement(new TextElement(() => T("pz.note.copy"), Color: Accent), Padding: 6f)),
        new ConditionalElement(() => _posingCtl.Failed, new TextElement(() => T("pz.failed"), Color: Muted)),
        new ConditionalElement(() => _posingCtl.Full, new TextElement(() => T("pz.full"), Color: Muted)),   // controller Q8
        PoseSection(),
        ExpressionSection(),
        LookSection(LookPart.Head, "pz.sub.head", "pz.help.head"),
        LookSection(LookPart.Eyes, "pz.sub.eyes", "pz.help.eyes"),
        RotateRow(),
        new RowElement(new HudElement[]
        {
            new CellElement(new ButtonElement(() => T("pz.reset"), () => _posingCtl.ResetPerson(), Enabled: PoseEnabled), Weight: 1f),
        }),
    }, Gap: 6f);

    private HudElement PersonRow() => new RowElement(new HudElement[]
    {
        new CellElement(new ButtonElement(() => "‹", () => _posingCtl.Cycle(-1)), Width: PersonArrowW),
        new CellElement(new RowElement(new HudElement[]
        {
            new TextElement(PersonName, Emphasis: true, NoWrap: true),
            new TextElement(PersonKindText, Color: Muted, NoWrap: true),   // 2-3 px baseline offset vs the bold name: framework menu text ignores FontSize (sandbox D3, framework issue)
        }, Gap: 6f, Justify: RowJustify.Center), Weight: 1f),
        new CellElement(new ButtonElement(() => "›", () => _posingCtl.Cycle(1)), Width: PersonArrowW),
    }, Gap: 8f);

    private string PersonName() => _posingCtl.Person is { Name.Length: > 0 } p ? p.Name : SubjectName();

    private string PersonKindText()
    {
        var kind = _posingCtl.Person?.Kind switch
        {
            PersonKind.Player => T("pz.kind.player"),
            PersonKind.Npc => T("pz.kind.npc"),
            _ => T("pz.kind.you"),
        };
        return _posingCtl.Loading ? kind + " · " + T("pz.loading") : kind;
    }

    private bool PoseEnabled() => _posingCtl.Available && !_posingCtl.Loading && !_posingCtl.Failed;
    private bool HasPose() => _posingCtl.State.Action is not null && PoseEnabled();

    private HudElement SubHeader(string key, string helpKey) => new RowElement(new HudElement[]
    {
        new TextElement(() => T(key).ToUpperInvariant(), Color: Muted, FontSize: SubFont),
        new SpacerElement(),
        HelpDot(key, () => T(key), () => T(helpKey)),
    }, Gap: 6f);

    private HudElement PoseSection() => new ColumnElement(new HudElement[]
    {
        SubHeader("fc.group.pose", "fc.help.pose"),
        new RowElement(new HudElement[]
        {
            new CellElement(new SelectableElement(new TextElement(PoseLabel, NoWrap: true), () => _poseListOpen = !_poseListOpen), Weight: 1f),
            new CellElement(new ButtonElement(() => _posingCtl.State.Playing ? "❚❚" : "▶", () => _posingCtl.TogglePlay(), Enabled: HasPose), Width: 34f),
            new CellElement(new ButtonElement(() => "↺", () => _posingCtl.Restart(), Enabled: HasPose), Width: 28f),
        }, Gap: 6f),
        new ConditionalElement(() => _poseListOpen, PoseList()),
        new RowElement(new HudElement[]
        {
            new CellElement(SliderRow(() => T("pz.moment"),
                new SliderElement(() => _posingCtl.State.Moment, v => _posingCtl.SetMoment(v), 0f, 1f, Enabled: HasPose),
                () => _loc.TFormat("pz.unit.percent", F(_posingCtl.State.Moment * 100f, "0")), () => _posingCtl.SetMoment(0f), HasPose), Weight: 1f),
            HelpDot("pz.moment", () => T("pz.moment"), () => T("pz.help.moment")),
        }, Gap: 6f),
        new ConditionalElement(() => _posingCtl.ShowClothHint, new TextElement(() => T("pz.hint.cloth"), Color: Muted)),
    }, Gap: 4f);

    private string PoseLabel() => (_posingCtl.State.Action?.Name ?? T("pz.pose.pick")) + (_poseListOpen ? "  ▾" : "  ▸");

    /// <summary>The 1.1 Pose list (search, ★ favourites, virtual list) — now the picker inside Pose.</summary>
    private HudElement PoseList() => new ColumnElement(new HudElement[]
    {
        LabeledRow(() => T("fc.search"), new InputElement(() => _emoteQuery, SetEmoteQuery, Width: 220f, OnChange: SetEmoteQuery)),
        new ConditionalElement(() => EmoteView().Count > 0,
            new VirtualListElement(() => EmoteView().Count, EmoteRowHeight, BuildEmotePool(), first => _emoteFirst = first, Height: 220f)
                { ResetScroll = TakeEmoteScrollReset },
            new TextElement(() => _services.Emotes.Unlocked.Count == 0 ? T("fc.pose.none") : T("fc.pose.empty"), Color: Muted)),
    }, Gap: 4f);

    private HudElement ExpressionSection() => new ColumnElement(new HudElement[]
    {
        SubHeader("pz.sub.expression", "pz.help.expression"),
        new RowElement(new HudElement[]
        {
            new CellElement(new ButtonElement(() => "‹", () => _posingCtl.CycleExpression(-1), Enabled: PoseEnabled), Width: PersonArrowW),
            new CellElement(new TextElement(ExpressionLabel, Align: TextAlign.Center, NoWrap: true), Weight: 1f),
            new CellElement(new ButtonElement(() => "›", () => _posingCtl.CycleExpression(1), Enabled: PoseEnabled), Width: PersonArrowW),
            new CellElement(new ButtonElement(() => T("pz.hold"), () => _posingCtl.ToggleHold(), Enabled: PoseEnabled,
                Active: () => _posingCtl.State.Hold), Width: LockW),
        }, Gap: 6f),
    }, Gap: 4f);

    private string ExpressionLabel() => _posingCtl.ExpressionName is { Length: > 0 } name ? name : T("pz.expression.none");

    private HudElement LookSection(LookPart part, string key, string helpKey) => new ColumnElement(new HudElement[]
    {
        SubHeader(key, helpKey),
        new RowElement(new HudElement[]
        {
            LookModeButton(part, LookMode.Default, "pz.look.default"),
            LookModeButton(part, LookMode.Lens, "pz.look.lens"),
            LookModeButton(part, LookMode.Free, "pz.look.free"),
            new CellElement(new ButtonElement(() => T("pz.lock"), () => _posingCtl.ToggleLock(part), Enabled: PoseEnabled,
                Active: () => _posingCtl.State.Locked(part)), Weight: 1f),   // fills the row's last 95 px (sandbox: fixed 90 left a 5 px gap)
        }, Gap: 4f),
        new ConditionalElement(() => _posingCtl.State.Mode(part) == LookMode.Free, AimPad(part)),
    }, Gap: 4f);

    private const float LookSegW = 92f;   // equal segments (sandbox D4: widths followed the labels, 103/89/88 in en)

    private HudElement LookModeButton(LookPart part, LookMode mode, string key) => new CellElement(new ButtonElement(
        () => T(key), () => _posingCtl.SetLook(part, mode), Enabled: PoseEnabled,
        Active: () => _posingCtl.State.Mode(part) == mode), Width: LookSegW);

    private HudElement AimPad(LookPart part) => new ColumnElement(new HudElement[]
    {
        PadRow(AimButton(part, "▲", 0, 1)),
        PadRow(AimButton(part, "◀", -1, 0), AimButton(part, "●", 0, 0), AimButton(part, "▶", 1, 0)),
        PadRow(AimButton(part, "▼", 0, -1)),
    }, Gap: 2f);

    private static HudElement PadRow(params HudElement[] buttons) => new RowElement(buttons, Gap: 2f, Justify: RowJustify.Center);

    private HudElement AimButton(LookPart part, string glyph, int dx, int dy) =>
        new CellElement(new ButtonElement(() => glyph, () => _posingCtl.Aim(part, dx, dy), Enabled: PoseEnabled), Width: PadW);

    private HudElement RotateRow() => new RowElement(new HudElement[]
    {
        new CellElement(SliderRow(() => T("pz.rotate"),
            new SliderElement(() => _posingCtl.State.Yaw, v => _posingCtl.SetYaw(v), -PosingController.MaxYaw, PosingController.MaxYaw, Enabled: PoseEnabled),
            YawText, () => _posingCtl.SetYaw(0f), PoseEnabled), Weight: 1f),
        HelpDot("pz.rotate", () => T("pz.rotate"), () => T("pz.help.rotate")),
    }, Gap: 6f);

    private string YawText()
    {
        var yaw = _posingCtl.State.Yaw;
        return _loc.TFormat("pz.unit.degrees", (yaw >= 0.5f ? "+" : "") + F(yaw, "0"));
    }
}
