using System;
using System.Collections.Generic;
using System.Globalization;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.Lights;

namespace Stellar.PhotoStudio;

// Lights tab (lights spec 2026-10-03; approved mockup v2 2026-10-03-lights-mockup.html; owner: "color should be selectable"):
// Lamps (list ≤ 8, ＋ at camera, selected lamp colour/strength/range/placement, Move here, Duplicate, Light people) and Person
// light (‹ › person, key direction/height, rim colour/strength). Colour = the Look tab's recipe (swatch + Edit → the
// framework ColorPickerElement + ↺) plus 8 quick swatches. Every control is a lambda read when it fires.
public sealed partial class Plugin
{
    private const int LampSlots = LightsController.MaxLamps;
    private bool _editingLampColor, _editingRimColor;

    private static readonly RgbColor[] QuickColors =
    {
        new(1f, 0.75f, 0.47f), new(1f, 0.91f, 0.76f), new(1f, 1f, 1f), new(0.73f, 0.84f, 1f),
        new(0.47f, 0.67f, 1f), new(0.75f, 0.55f, 1f), new(1f, 0.48f, 0.66f), new(0.48f, 1f, 0.77f),
    };

    private HudElement BuildLightsTab() => new ColumnElement(new HudElement[]
    {
        FoldGroup("lt.group.lamps", "lt.help.lamps", () => _settings.LampsOpen, o => _settings.SetLampsOpen(o), LampsBody()),
        new SeparatorElement(),
        FoldGroup("lt.group.person", "lt.help.person", () => _settings.PersonLightOpen, o => _settings.SetPersonLightOpen(o), PersonLightBody()),
    }, Gap: 8f);

    private HudElement[] LampsBody()
    {
        var rows = new List<HudElement>();
        for (var i = 0; i < LampSlots; i++) rows.Add(LampRow(i));
        rows.Add(new RowElement(new HudElement[]
        {
            new CellElement(new ButtonElement(() => T("lt.addAtCamera"), OnClick: () => LampToast(_lights.AddAtCamera()),
                Enabled: () => _lights.CanAdd), Weight: 1f),
            new TextElement(() => _loc.TFormat("lt.count", _lights.Count, LampSlots), Color: Muted),
        }, Gap: 6f));
        rows.Add(new ConditionalElement(() => _lights.SelectedLamp is not null, SelectedLampEditor()));
        rows.Add(SliderRow(() => T("lt.lightPeople"),
            new SliderElement(() => _lights.PeopleLevel, v => _lights.SetPeopleLevel(v), 0f, 20f),
            () => F(_lights.PeopleLevel, "0.0"), () => _lights.SetPeopleLevel(LightsController.DefaultPeopleLevel)));
        rows.Add(new TextElement(() => T("lt.lightPeople.note"), Color: Muted, FontSize: SubFont));
        rows.Add(HelpToggle("lights.markers", () => _settings.ShowLampMarkers, on => _settings.SetShowLampMarkers(on),
            new HelpText(() => T("lt.showMarkers"), () => T("lt.help.markers"))));
        return rows.ToArray();
    }

    private HudElement LampRow(int i) => new ConditionalElement(() => i < _lights.Count, new RowElement(new HudElement[]
    {
        new SwatchElement(() => LampColor(i), Size: 14f),
        new CellElement(new SelectableElement(new TextElement(() => LampName(i), Color: () => _lights.SelectedIndex == i ? Accent() : Normal()),
            () => _lights.Select(i)), Weight: 1f),
        new ToggleElement(() => "", () => _lights.LampAt(i)?.On ?? false, on => LampToast(_lights.SetOn(i, on))),
        new ButtonElement(() => "✕", OnClick: () => _lights.Remove(i), Width: 28f),
    }, Gap: 6f));

    private HudElement SelectedLampEditor() => new ColumnElement(new HudElement[]
    {
        new TextElement(() => _lights.SelectedLamp is { } l ? LampName(l.Number - 1) : "", Emphasis: true),
        ColorRow("lt.color", () => _lights.SelectedLamp?.Color ?? LightsController.DefaultColor, c => _lights.SetColor(c),
            () => _editingLampColor, v => _editingLampColor = v, LightsController.DefaultColor),
        SliderRow(() => T("lt.strength"),
            new SliderElement(() => _lights.SelectedLamp?.Strength ?? 0f, v => _lights.SetStrength(v), 0f, LightLimits.MaxStrength),
            () => F(_lights.SelectedLamp?.Strength ?? 0f, "0"), () => _lights.SetStrength(LightsController.DefaultStrength)),
        SliderRow(() => T("lt.range"),
            new SliderElement(() => _lights.SelectedLamp?.Range ?? 0f, v => _lights.SetRange(v), LightLimits.MinRange, LightLimits.MaxRange),
            () => _loc.TFormat("fc.unit.metres", F(_lights.SelectedLamp?.Range ?? 0f, "0.0")), () => _lights.SetRange(LightsController.DefaultRange)),
        PlacementRow("lt.around", p => p.Around, v => _lights.SetAround(v), LightsMath.MinAround, LightsMath.MaxAround, v => F(v, "+0;-0;0") + "°", 0f),
        PlacementRow("lt.height", p => p.Height, v => _lights.SetHeight(v), LightsMath.MinHeight, LightsMath.MaxHeight, v => _loc.TFormat("fc.unit.metres", F(v, "0.0")), 1.8f),
        PlacementRow("lt.distance", p => p.Distance, v => _lights.SetDistance(v), LightsMath.MinDistance, LightsMath.MaxDistance, v => _loc.TFormat("fc.unit.metres", F(v, "0.0")), 2f),
        new ConditionalElement(() => _lights.SelectedPlacement is null, new TextElement(() => T("lt.noPerson"), Color: Muted, FontSize: SubFont)),
        new RowElement(new HudElement[]
        {
            new CellElement(new ButtonElement(() => T("lt.moveHere"), OnClick: () => LampToast(_lights.MoveSelectedToCamera())), Weight: 1f),
            new CellElement(new ButtonElement(() => T("lt.duplicate"), OnClick: () => LampToast(_lights.Duplicate()),
                Enabled: () => _lights.CanAdd), Weight: 1f),
        }, Gap: 6f),
    }, Gap: 4f);

    private HudElement PlacementRow(string key, Func<LampPlacement, float> get, Func<float, LightsResult> set, float min, float max,
        Func<float, string> text, float reset) => SliderRow(() => T(key),
        new SliderElement(() => _lights.SelectedPlacement is { } p ? get(p) : reset, v => set(v), min, max,
            Enabled: () => _lights.SelectedPlacement is not null),
        () => _lights.SelectedPlacement is { } p ? text(get(p)) : "—", () => set(reset), () => _lights.SelectedPlacement is not null);

    private HudElement[] PersonLightBody() => new HudElement[]
    {
        PersonRow(),   // Plugin.Panel.Person.cs — the same ‹ name kind › selection as the Camera tab
        new TextElement(() => T("lt.sub.key"), Color: Muted, FontSize: SubFont),
        HelpToggle("lt.keyOn", () => _lights.SelectedPersonLight.KeyOn, on => _lights.SetKeyOn(on),
            new HelpText(() => T("lt.keyOn"), () => T("lt.help.key"))),
        SliderRow(() => T("lt.keyDirection"),
            new SliderElement(() => _lights.SelectedPersonLight.KeyDirection, v => _lights.SetKeyDirection(v), -180f, 180f),
            () => F(_lights.SelectedPersonLight.KeyDirection, "+0;-0;0") + "°", () => _lights.SetKeyDirection(PersonLightState.Default.KeyDirection)),
        SliderRow(() => T("lt.keyHeight"),
            new SliderElement(() => _lights.SelectedPersonLight.KeyHeight, v => _lights.SetKeyHeight(v), LightsMath.MinKeyHeight, LightsMath.MaxKeyHeight),
            () => F(_lights.SelectedPersonLight.KeyHeight, "+0;-0;0") + "°", () => _lights.SetKeyHeight(PersonLightState.Default.KeyHeight)),
        new TextElement(() => T("lt.sub.rim"), Color: Muted, FontSize: SubFont),
        HelpToggle("lt.rimOn", () => _lights.SelectedPersonLight.RimOn, on => _lights.SetRimOn(on),
            new HelpText(() => T("lt.rimOn"), () => T("lt.help.rim"))),
        ColorRow("lt.rimColor", () => _lights.SelectedPersonLight.RimColor, c => _lights.SetRimColor(c),
            () => _editingRimColor, v => _editingRimColor = v, PersonLightState.Default.RimColor),
        SliderRow(() => T("lt.rimStrength"),
            new SliderElement(() => _lights.SelectedPersonLight.RimStrength, v => _lights.SetRimStrength(v), 0f, LightLimits.MaxRimStrength),
            () => F(_lights.SelectedPersonLight.RimStrength, "0.00"), () => _lights.SetRimStrength(PersonLightState.Default.RimStrength)),
        new CellElement(new ButtonElement(() => T("lt.resetPerson"), OnClick: () => _lights.ResetPersonLight()), Weight: 1f),
    };

    // The Look tab's colour recipe (Plugin.Panel.Look.cs ColorGroup) + a row of quick colours.
    private HudElement ColorRow(string key, Func<RgbColor> get, Func<RgbColor, LightsResult> set, Func<bool> editing,
        Action<bool> setEditing, RgbColor reset) => new ColumnElement(new HudElement[]
    {
        LabeledRow(() => T(key),
            new SwatchElement(() => ToRgba(get()), Size: 16f),
            new ButtonElement(() => editing() ? T("ps.done") : T("ps.edit"), OnClick: () => setEditing(!editing()), Width: 56f),
            new ButtonElement(() => "↺", OnClick: () => set(reset), Width: 28f)),
        new RowElement(QuickSwatches(set), Gap: 4f),
        new ConditionalElement(editing, new ColorPickerElement(() => ToRgba(get()), c => set(new RgbColor(c.R, c.G, c.B))) { ShowAlpha = false }),   // lights have no alpha (review I-7)
    }, Gap: 4f);

    private static HudElement[] QuickSwatches(Func<RgbColor, LightsResult> set)
    {
        var cells = new HudElement[QuickColors.Length + 1];
        cells[0] = new SpacerElement(Width: LabelW);
        for (var i = 0; i < QuickColors.Length; i++)
        {
            var c = QuickColors[i];
            cells[i + 1] = new SelectableElement(new SwatchElement(() => ToRgba(c), Size: 18f), () => set(c));
        }
        return cells;
    }

    private static ColorRgba ToRgba(RgbColor c) => new(c.R, c.G, c.B, 1f);

    private ColorRgba LampColor(int i) => _lights.LampAt(i) is { } l ? ToRgba(l.Color) : new ColorRgba(0f, 0f, 0f, 0f);   // one row, no list rebuild

    /// <summary>Save's enabled state, refreshed at the panel's ~10 Hz poll (Plugin.Posing.cs) — CanSave reads the game
    /// (lights compare), so the Enabled lambda must not call it every refresh.</summary>
    private bool _canSaveCached;

    private void RefreshCanSave() => _canSaveCached = _presetSession.CanSave;

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
