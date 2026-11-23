using System;
using System.Globalization;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Lights;

namespace Stellar.PhotoStudio;

// Lights section (lights spec 2026-10-03; approved prototype "Lights"): LAMPS (lamp tiles [swatch · name · On/Off · ✕],
// "+ Lamp at camera" + "n / 8" in the caption, Light people + its note, Show markers), the SELECTED lamp in its own panel
// (colour + quick colours + Edit, strength / range / around / height / distance, Move here / Duplicate) and the PERSON
// LIGHT fold (‹ › person, key direction / height, rim colour / strength). Colour = swatch + Edit → the framework
// ColorPickerElement + ↺, plus 8 quick swatches. Every control is a lambda read when it fires.
public sealed partial class Plugin
{
    private const string SecLights = "ps.tab.lights";
    private const int LampSlots = LightsController.MaxLamps;
    private const float QuickSwatch = 16f;
    private bool _editingLampColor, _editingRimColor;

    private static readonly RgbColor[] QuickColors =
    {
        new(1f, 0.75f, 0.47f), new(1f, 0.91f, 0.76f), new(1f, 1f, 1f), new(0.73f, 0.84f, 1f),
        new(0.47f, 0.67f, 1f), new(0.75f, 0.55f, 1f), new(1f, 0.48f, 0.66f), new(0.48f, 1f, 0.77f),
    };

    private HudElement BuildLightsSection() => SectionColumn(
        LivePanel(UpperLoc("lt.group.lamps"), LampCountText,
            new HudElement[] { HelpButton(new HelpKeys("lt.group.lamps", "lt.help.lamps", SecLights)) }, LampsBody()),
        new ConditionalElement(() => _lights.SelectedLamp is not null,
            LivePanel(SelectedLampCaption, Loc("ps.lt.selected"), null, SelectedLampBody())),
        FoldPanel("lt.group.person", null, new FoldFlag(() => _settings.PersonLightOpen, o => _settings.SetPersonLightOpen(o)),
            new HudElement[] { HelpButton(new HelpKeys("lt.group.person", "lt.help.person", SecLights)) }, PersonLightBody()));

    private HudElement[] LampsBody()
    {
        var rows = new HudElement[LampSlots + 3];
        for (var i = 0; i < LampSlots; i++) rows[i] = LampRow(i);
        rows[LampSlots] = new RowElement(new HudElement[]
        {
            Pill(PillKind.Primary, "ps.lt.addLamp", () => LampToast(_lights.AddAtCamera()), 126f, enabled: () => _lights.CanAdd),
            new CellElement(SettingToggle("lt.showMarkers", () => _settings.ShowLampMarkers, on => _settings.SetShowLampMarkers(on),
                new HelpKeys("lt.showMarkers", "lt.help.markers", SecLights, "lt.group.lamps")), Weight: 1f),
        }, Gap: 12f);
        rows[LampSlots + 1] = LightSlider("lt.lightPeople", () => _lights.PeopleLevel, v => _lights.SetPeopleLevel(v), 0f, 20f,
            ValueText(() => _lights.PeopleLevel, v => F(v, "0.0")), LightsController.DefaultPeopleLevel, 0.1f);
        rows[LampSlots + 2] = NoteLine(Loc("lt.lightPeople.note"));
        return rows;
    }

    // [swatch] [name — click selects] [On/Off] [✕]: a raised tile, accent-washed while selected (prototype .tile.sel).
    private HudElement LampRow(int i) => new ConditionalElement(() => i < _lights.Count, ListTile(new RowElement(new HudElement[]
    {
        new SpacerElement(4f, ListTileH),
        new SwatchElement(() => LampColor(i), Size: 14f),
        new CellElement(new SelectableElement(new TextElement(LampNameText(i), Color: () => TextColor(), NoWrap: true) { Ellipsize = true },
            OnClick: () => _lights.Select(i)) { Bare = true }, Weight: 1f),
        Chip(() => _lights.LampAt(i)?.On ?? false, on => LampToast(_lights.SetOn(i, on)), ChipStyle.OnOff, surface: TileColor),
        IconButton("✕", () => _lights.Remove(i), IconButtonKind.Remove, surface: TileColor, size: ChipH),
        new SpacerElement(1f, ListTileH),
    }, Gap: 8f), selected: () => _lights.SelectedIndex == i));

    private HudElement[] SelectedLampBody() => new HudElement[]
    {
        ColorRow("lt.color", () => _lights.SelectedLamp?.Color ?? LightsController.DefaultColor, c => _lights.SetColor(c),
            () => _editingLampColor, v => _editingLampColor = v, LightsController.DefaultColor),
        LightSlider("lt.strength", () => _lights.SelectedLamp?.Strength ?? 0f, v => _lights.SetStrength(v), 0f, LightLimits.MaxStrength,
            ValueText(() => _lights.SelectedLamp?.Strength ?? 0f, v => F(v, "0")), LightsController.DefaultStrength, 1f),
        LightSlider("lt.range", () => _lights.SelectedLamp?.Range ?? 0f, v => _lights.SetRange(v), LightLimits.MinRange, LightLimits.MaxRange,
            ValueText(() => _lights.SelectedLamp?.Range ?? 0f, v => _loc.TFormat("fc.unit.metres", F(v, "0.0")), "fc.unit.metres"),
            LightsController.DefaultRange, 0.1f),
        PlacementRow("lt.around", p => p.Around, v => _lights.SetAround(v), LightsMath.MinAround, LightsMath.MaxAround, v => F(v, "+0;-0;0") + "°", 0f, 1f),
        PlacementRow("lt.height", p => p.Height, v => _lights.SetHeight(v), LightsMath.MinHeight, LightsMath.MaxHeight,
            v => _loc.TFormat("fc.unit.metres", F(v, "0.0")), 1.8f, 0.1f),
        PlacementRow("lt.distance", p => p.Distance, v => _lights.SetDistance(v), LightsMath.MinDistance, LightsMath.MaxDistance,
            v => _loc.TFormat("fc.unit.metres", F(v, "0.0")), 2f, 0.1f),
        new ConditionalElement(() => _lights.SelectedPlacement is null, NoteLine(Loc("lt.noPerson"))),
        PillPair(
            Pill(PillKind.Neutral, "lt.moveHere", () => LampToast(_lights.MoveSelectedToCamera()), PillPairLabelW),
            Pill(PillKind.Neutral, "lt.duplicate", () => LampToast(_lights.Duplicate()), PillPairLabelW, enabled: () => _lights.CanAdd)),
    };

    private HudElement LightSlider(string key, Func<float> get, Action<float> set, float min, float max, Func<string> value,
        float reset, float step, Func<bool>? enabled = null)
        => SettingSlider(key, KitSlider(get, v => set(v), min, max, enabled), value, () => set(reset), new SliderOpts(Step: step));

    // A placement slider: relative to the selected person, so it is recessed (shows "—") with nobody selected.
    private HudElement PlacementRow(string key, Func<LampPlacement, float> get, Func<float, LightsResult> set, float min, float max,
        Func<float, string> text, float reset, float step)
    {
        var cache = new Cached<(bool, float, string)>(k => k.Item1 ? text(k.Item2) : "—");
        return SettingSlider(key,
            KitSlider(() => _lights.SelectedPlacement is { } p ? get(p) : reset, v => set(v), min, max, () => _lights.SelectedPlacement is not null),
            () => _lights.SelectedPlacement is { } p ? cache.Get((true, get(p), T("fc.unit.metres"))) : cache.Get((false, 0f, string.Empty)),
            () => set(reset), new SliderOpts(Step: step));
    }

    private HudElement[] PersonLightBody() => new HudElement[]
    {
        PersonBar(),   // Plugin.Section.Pose.cs — the same ‹ name kind › selection as the Pose section
        SettingToggle("lt.keyOn", () => _lights.SelectedPersonLight.KeyOn, on => _lights.SetKeyOn(on),
            new HelpKeys("lt.keyOn", "lt.help.key", SecLights, "lt.group.person")),
        LightSlider("lt.keyDirection", () => _lights.SelectedPersonLight.KeyDirection, v => _lights.SetKeyDirection(v), -180f, 180f,
            ValueText(() => _lights.SelectedPersonLight.KeyDirection, v => F(v, "+0;-0;0") + "°"), PersonLightState.Default.KeyDirection, 1f),
        LightSlider("lt.keyHeight", () => _lights.SelectedPersonLight.KeyHeight, v => _lights.SetKeyHeight(v), LightsMath.MinKeyHeight,
            LightsMath.MaxKeyHeight, ValueText(() => _lights.SelectedPersonLight.KeyHeight, v => F(v, "+0;-0;0") + "°"),
            PersonLightState.Default.KeyHeight, 1f),
        SettingToggle("lt.rimOn", () => _lights.SelectedPersonLight.RimOn, on => _lights.SetRimOn(on),
            new HelpKeys("lt.rimOn", "lt.help.rim", SecLights, "lt.group.person")),
        ColorRow("lt.rimColor", () => _lights.SelectedPersonLight.RimColor, c => _lights.SetRimColor(c),
            () => _editingRimColor, v => _editingRimColor = v, PersonLightState.Default.RimColor),
        LightSlider("lt.rimStrength", () => _lights.SelectedPersonLight.RimStrength, v => _lights.SetRimStrength(v), 0f,
            LightLimits.MaxRimStrength, ValueText(() => _lights.SelectedPersonLight.RimStrength, v => F(v, "0.00")),
            PersonLightState.Default.RimStrength, 0.01f),
        Pill(PillKind.Neutral, "lt.resetPerson", () => _lights.ResetPersonLight()),
    };

    /// <summary>[label] [swatch] [8 quick colours] … [Edit / Done] [↺] — then the framework colour picker while editing.
    /// Lights have no alpha (review I-7).</summary>
    private HudElement ColorRow(string key, Func<RgbColor> get, Func<RgbColor, LightsResult> set, Func<bool> editing,
        Action<bool> setEditing, RgbColor reset) => new ColumnElement(new HudElement[]
    {
        new RowElement(new HudElement[]
        {
            new SpacerElement(0f, PillH),
            RowLabel(key, null, RowLabelW),
            new SwatchElement(() => ToRgba(get()), Size: 20f),
            new RowElement(QuickSwatches(set), Gap: 4f),
            new CellElement(new SpacerElement(0f, 1f), Weight: 1f),
            Pill(PillKind.Neutral, () => T(editing() ? "ps.done" : "ps.edit"), () => setEditing(!editing()), 52f),
            IconImageButton("reset", () => set(reset)),
            new SpacerElement(HelpDotSize, HelpDotSize),
        }, Gap: RowGap),
        new ConditionalElement(editing, new ColorPickerElement(() => ToRgba(get()), c => set(new RgbColor(c.R, c.G, c.B))) { ShowAlpha = false }),
    }, Gap: 6f);

    private static HudElement[] QuickSwatches(Func<RgbColor, LightsResult> set)
    {
        var cells = new HudElement[QuickColors.Length];
        for (var i = 0; i < QuickColors.Length; i++)
        {
            var c = QuickColors[i];
            cells[i] = new SelectableElement(new SwatchElement(() => ToRgba(c), Size: QuickSwatch), () => set(c)) { Bare = true };
        }
        return cells;
    }

    private static ColorRgba ToRgba(RgbColor c) => new(c.R, c.G, c.B, 1f);

    private ColorRgba LampColor(int i) => _lights.LampAt(i) is { } l ? ToRgba(l.Color) : new ColorRgba(0f, 0f, 0f, 0f);   // one row, no list rebuild

    /// <summary>Save's enabled state, refreshed at the panel's ~10 Hz poll (Plugin.Posing.cs) — CanSave reads the game
    /// (lights compare), so the Enabled lambda must not call it every refresh.</summary>
    private bool _canSaveCached;

    private void RefreshCanSave() => _canSaveCached = _presetSession.CanSave;

    private Func<string> LampNameText(int i)
    {
        var cache = new Cached<string>(_ => LampName(i));
        return () => cache.Get(T("lt.lampRole"));
    }

    private Cached<(int, string)>? _selectedLampCaption;

    private string SelectedLampCaption()
    {
        _selectedLampCaption ??= new Cached<(int, string)>(k => k.Item1 < 0 ? string.Empty : LampName(k.Item1).ToUpperInvariant());
        return _selectedLampCaption.Get((_lights.SelectedIndex, T("lt.lampRole")));
    }

    private Cached<(int, string)>? _lampCount;

    private string LampCountText()
    {
        _lampCount ??= new Cached<(int, string)>(k => _loc.TFormat("lt.count", k.Item1, LampSlots));
        return _lampCount.Get((_lights.Count, T("lt.count")));
    }

    private string LampName(int i)
    {
        var number = (i + 1).ToString(CultureInfo.InvariantCulture);
        return LightsController.RoleOf(i) switch
        {
            LampRole.Key => _loc.TFormat("lt.lampRole", number, T("lt.role.key")),
            LampRole.Fill => _loc.TFormat("lt.lampRole", number, T("lt.role.fill")),
            LampRole.Back => _loc.TFormat("lt.lampRole", number, T("lt.role.back")),
            _ => _loc.TFormat("lt.lamp", number),
        };
    }

    /// <summary>Toasts the two results the player can act on (spec § 2); the rest are silent.</summary>
    private void LampToast(LightsResult r)
    {
        var key = r switch
        {
            LightsResult.Full => "lt.toast.full",
            LightsResult.NoCamera => "lt.toast.noCamera",
            LightsResult.Unavailable => "lt.toast.unavailable",
            _ => null,
        };
        if (key is not null) _services.Notifications.Notify(T(key), NotificationKind.Warning);
        _panelWin.MarkDirty();
    }
}
