using System;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Posing;

namespace Stellar.PhotoStudio;

// Pose section (NEW in 2.0.0 — the 1.x Camera › Person fold; posing spec 2026-10-02 § 3, approved prototype "Pose"):
// PERSON ‹ name kind ›, POSE (picker + pause/play + restart, the search / ★ favourites list, Moment), EXPRESSION (‹ name ›
// + Hold), HEAD AND EYES (Default | Lens | Free + Lock; the aim pad in Free) and ROTATE + Reset this person. Posing works
// with the free camera off (scene-stays spec § 5); only being out of the world turns it off. The pose list is
// Plugin.Emotes.cs. Every control is a lambda read when it fires.
public sealed partial class Plugin
{
    private const string SecPose = "ps.sec.pose";
    private const float AimGridSize = 112f;
    private static readonly ChipStyle HoldChip = new("pz.hold", "pz.hold", 72f);
    private static readonly ChipStyle LockChip = new("ps.kit.locked", "pz.lock", 72f);
    private static readonly LookMode[] LookModes = { LookMode.Default, LookMode.Lens, LookMode.Free };
    private static readonly float[] LookSegW = { 72f, 44f, 44f };
    private static readonly float[] AimStepSegW = { 48f, 56f, 56f };
    private bool _poseListOpen;

    private HudElement BuildPoseSection() => new ConditionalElement(() => _services.Posing.IsAvailable,
        SectionColumn(PersonPanel(), PosePanel(), ExpressionPanel(), HeadEyesPanel(), RotatePanel()),
        SectionPanel("pz.group.person", null, new HudElement[] { NoteLine(Loc("pz.off")) }));

    private HudElement PersonPanel() => SectionPanel("pz.group.person", null, new HudElement[]
    {
        PersonBar(),
        // Click-to-select needs the free camera's picker; off the camera the hint points at ‹ › instead.
        NoteLine(() => T(_freeCam.Active ? "pz.hint.select" : "pz.hint.selectOff")),
        new ConditionalElement(() => _posingCtl.IsCopy, NoteLine(Loc("pz.note.copy"), AccentColor)),
        new ConditionalElement(() => _posingCtl.Failed, NoteLine(Loc("pz.failed"))),
        new ConditionalElement(() => _posingCtl.Full, NoteLine(Loc("pz.full"))),   // controller Q8
    });

    /// <summary>‹ [bold name · muted kind] › in a raised tile — also the Lights › Person light selector.</summary>
    private HudElement PersonBar() => new BubbleElement(new RowElement(new HudElement[]
        {
            IconButton("‹", () => _posingCtl.Cycle(-1), surface: TileColor),
            new CellElement(new RowElement(new HudElement[]
            {
                new TextElement(PersonName, Color: () => TextColor(), NoWrap: true, FontSize: 13) { Bold = true, Ellipsize = true },
                new TextElement(PersonKindText, Color: () => MutedColor(), NoWrap: true),
            }, Gap: 6f, Justify: RowJustify.Center), Weight: 1f),
            IconButton("›", () => _posingCtl.Cycle(1), surface: TileColor),
        }, Gap: 8f))
    {
        FillColorFn = () => Fade(TileColor()), CornerRadius = TileRadius, PaddingX = 4f, PaddingY = 4f,
    };

    private string PersonName() => _posingCtl.Person is { Name.Length: > 0 } p ? p.Name : SubjectName();

    private Cached<(int, bool, string)>? _personKind;

    private string PersonKindText()
    {
        // "you" only for the real local player — an unknown (far-picked) person is never "you" (review).
        var kind = _posingCtl.SubjectIsSelf ? 0 : _posingCtl.Person?.Kind == PersonKind.Npc ? 1 : 2;
        _personKind ??= new Cached<(int, bool, string)>(k =>
        {
            var text = T(k.Item1 == 0 ? "pz.kind.you" : k.Item1 == 1 ? "pz.kind.npc" : "pz.kind.player");
            return "· " + (k.Item2 ? text + " · " + T("pz.loading") : text);
        });
        return _personKind.Get((kind, _posingCtl.Loading, T("pz.kind.you")));
    }

    private bool PoseEnabled() => _posingCtl.Available && !_posingCtl.Loading && !_posingCtl.Failed && !_posingCtl.Full;
    private bool HasPose() => _posingCtl.State.Action is not null && PoseEnabled();

    private HudElement PosePanel() => SectionPanel("fc.group.pose", null, new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new CellElement(PickerPill(PoseLabel, () => _poseListOpen && PoseEnabled(),
                () => _poseListOpen = !_poseListOpen, PoseEnabled), Weight: 1f),
            new ConditionalElement(() => _posingCtl.State.Playing,
                IconImageButton("pause", () => _posingCtl.TogglePlay(), enabled: HasPose),
                IconImageButton("play", () => _posingCtl.TogglePlay(), enabled: HasPose)),
            // Off for a detected pose you have not unlocked.
            IconImageButton("reset", () => _posingCtl.Restart(), enabled: () => HasPose() && _posingCtl.CanRestart),
            HelpButton(new HelpKeys("fc.group.pose", "fc.help.pose", SecPose)),
        }, Gap: RowGap),
        new ConditionalElement(() => _poseListOpen && PoseEnabled(), PoseList()),   // Plugin.Emotes.cs
        SettingSlider("pz.moment", KitSlider(() => _posingCtl.State.Moment, v => _posingCtl.SetMoment(v), 0f, 1f, HasPose),
            ValueText(() => _posingCtl.State.Moment, v => _loc.TFormat("pz.unit.percent", F(v * 100f, "0")), "pz.unit.percent"),
            () => _posingCtl.SetMoment(0f), new SliderOpts(new HelpKeys("pz.moment", "pz.help.moment", SecPose, "fc.group.pose"))),
        // "Press Space…" names the free camera's key; off the camera the Scene panel's Freeze button is the way.
        new ConditionalElement(() => _posingCtl.ShowClothHint && !SceneFrozen && _freeCam.Active, NoteLine(Loc("pz.hint.cloth"))),
    });

    private string PoseLabel() => _posingCtl.State.Action?.Name ?? T("pz.pose.pick");

    private HudElement ExpressionPanel() => SectionPanel("pz.sub.expression", null, new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            IconButton("‹", () => _posingCtl.CycleExpression(-1), enabled: PoseEnabled),
            new CellElement(new BubbleElement(new RowElement(new HudElement[]
                {
                    new SpacerElement(0f, PillH),
                    new CellElement(new TextElement(ExpressionLabel, Color: () => PoseEnabled() ? TextColor() : MutedColor(),
                        Align: TextAlign.Center, NoWrap: true) { Ellipsize = true }, Weight: 1f),
                }, Gap: 0.01f))
            {
                FillColorFn = () => Fade(TileColor()), CornerRadius = TileRadius, PaddingX = 8f, PaddingY = 0f,
            }, Weight: 1f),
            IconButton("›", () => _posingCtl.CycleExpression(1), enabled: PoseEnabled),
            Chip(() => _posingCtl.State.Hold, _ => _posingCtl.ToggleHold(), HoldChip, PoseEnabled),
            HelpButton(new HelpKeys("pz.sub.expression", "pz.help.expression", SecPose)),
        }, Gap: RowGap),
    });

    private string ExpressionLabel() => _posingCtl.ExpressionName is { Length: > 0 } name ? name : T("pz.expression.none");

    private HudElement HeadEyesPanel() => SectionPanel("ps.pose.headEyes", null, new HudElement[]
    {
        LookRow(LookPart.Head, "pz.sub.head", "pz.help.head"),
        new ConditionalElement(() => _posingCtl.State.Mode(LookPart.Head) == LookMode.Free, AimPad(LookPart.Head)),
        LookRow(LookPart.Eyes, "pz.sub.eyes", "pz.help.eyes"),
        new ConditionalElement(() => _posingCtl.State.Mode(LookPart.Eyes) == LookMode.Free, AimPad(LookPart.Eyes)),
    });

    /// <summary>[Head] [Default | Lens | Free] … [Lock] [?].</summary>
    private HudElement LookRow(LookPart part, string labelKey, string helpKey) => new RowElement(new HudElement[]
    {
        new SpacerElement(0f, PillH),
        RowLabel(labelKey, PoseEnabled, RowLabelW),
        Segmented(new[] { "pz.look.default", "pz.look.lens", "pz.look.free" }, LookSegW,
            () => Array.IndexOf(LookModes, _posingCtl.State.Mode(part)), i => _posingCtl.SetLook(part, LookModes[i]), PoseEnabled),
        new CellElement(new SpacerElement(0f, 1f), Weight: 1f),
        Chip(() => _posingCtl.State.Locked(part), _ => _posingCtl.ToggleLock(part), LockChip, PoseEnabled),
        HelpButton(new HelpKeys(labelKey, helpKey, SecPose, "ps.pose.headEyes")),
    }, Gap: RowGap);

    // Drag the dot on the grid (player request 2026-10-05: "rect area … drag and drop point"), or nudge with the arrows by
    // the chosen step; both write the same aim (PosingController.SetAim), so the dot follows the arrows and the readout.
    private HudElement AimPad(LookPart part) => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new XYPadElement(() => (_posingCtl.State.AimX(part), _posingCtl.State.AimY(part)),
                (x, y) => _posingCtl.SetAim(part, x, y), Enabled: PoseEnabled) { Size = AimGridSize },
            new ColumnElement(new HudElement[]
            {
                PadRow(AimButton(part, "▲", 0, 1)),
                PadRow(AimButton(part, "◀", -1, 0), AimButton(part, "●", 0, 0), AimButton(part, "▶", 1, 0)),
                PadRow(AimButton(part, "▼", 0, -1)),
                new RowElement(new HudElement[] { new TextElement(AimReadout(part), Color: () => MutedColor(), NoWrap: true) },
                    Gap: 0.01f, Justify: RowJustify.Center),
            }, Gap: 2f),
        }, Gap: 12f, Justify: RowJustify.Center),
        // Step: Fine · Normal · Coarse (shared by Head and Eyes, remembered as pose.aimStep).
        SettingRow("pz.aim.step", Segmented(new[] { "pz.aim.fine", "pz.aim.normal", "pz.aim.coarse" }, AimStepSegW,
            () => _settings.AimStepIndex, i => _settings.SetAimStepIndex(i), PoseEnabled), enabled: PoseEnabled),
    }, Gap: 6f);

    private Func<string> AimReadout(LookPart part)
    {
        var cache = new Cached<(float, float, string)>(k => _loc.TFormat("pz.aim.readout", AimSigned(k.Item1), AimSigned(k.Item2)));
        return () => cache.Get((_posingCtl.State.AimX(part), _posingCtl.State.AimY(part), T("pz.aim.readout")));
    }

    private static string AimSigned(float v) => (v >= 0.005f ? "+" : v <= -0.005f ? "−" : "") + F(Math.Abs(v), "0.00");

    private static HudElement PadRow(params HudElement[] buttons) => new RowElement(buttons, Gap: 2f, Justify: RowJustify.Center);

    private HudElement AimButton(LookPart part, string glyph, int dx, int dy) => IconButton(glyph,
        () => _posingCtl.Aim(part, dx, dy, PosingController.AimSteps[_settings.AimStepIndex]), enabled: PoseEnabled);

    private HudElement RotatePanel() => SectionPanel("pz.rotate", null, new HudElement[]
    {
        SettingSlider("pz.rotate",
            KitSlider(() => _posingCtl.State.Yaw, v => _posingCtl.SetYaw(v), -PosingController.MaxYaw, PosingController.MaxYaw, PoseEnabled),
            ValueText(() => _posingCtl.State.Yaw, v => _loc.TFormat("pz.unit.degrees", (v >= 0.5f ? "+" : "") + F(v, "0")), "pz.unit.degrees"),
            () => _posingCtl.SetYaw(0f), new SliderOpts(new HelpKeys("pz.rotate", "pz.help.rotate", SecPose), 1f)),
        // Also the way out of Failed / Loading.
        Pill(PillKind.Neutral, "pz.reset", () => _posingCtl.ResetPerson(), enabled: () => _posingCtl.Available),
    });
}
