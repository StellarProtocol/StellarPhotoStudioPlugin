using System;
using System.Globalization;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;
using Stellar.PhotoStudio.FreeCam;
using Stellar.PhotoStudio.Lights;

namespace Stellar.PhotoStudio;

// Lamp markers (lights spec § 3, approved mockup v2): one small PASSIVE window per lamp slot (click-through, no padding)
// holding a colour swatch + the lamp number, placed each frame over the lamp's screen position
// (ICameraOverride.TryProjectToScreen — the free camera or the game camera, whichever renders). UI only, so never in the
// photo. Shown while the free camera is on or the Lights tab is open, and "Show lamp markers" is on.
public sealed partial class Plugin
{
    private const float MarkerW = 40f, MarkerH = 22f;
    private const float MarkerMoveEpsilon = 0.5f;   // canvas units — SetRect only when the marker really moved
    private readonly IWindowControl?[] _markers = new IWindowControl?[LightsController.MaxLamps];
    private readonly (float X, float Y)[] _markerAt = new (float, float)[LightsController.MaxLamps];

    private bool MarkersWanted() => _settings.ShowLampMarkers && InWorld() && _lights.Count > 0 &&
        (_freeCam.Active || (_panelWin.IsShown && _settings.Tab == StudioTabs.Lights));

    /// <summary>Called from OnUpdate. Off: hides any marker (no allocation). On: one projection per lamp; a window is
    /// registered lazily per slot the first time it is needed and only re-placed when it moved.</summary>
    private void TickLampMarkers()
    {
        var wanted = MarkersWanted();
        for (var i = 0; i < _markers.Length; i++)
        {
            if (!wanted || _lights.LampAt(i) is not { } lamp) { HideMarker(i); continue; }
            if (!_services.CameraOverride.TryProjectToScreen(CameraMath.ToPos(lamp.Position), out var sp) || !sp.InFront)
            { HideMarker(i); continue; }
            PlaceMarker(i, ToCanvas(sp.X, _services.Framework.ScreenWidth, _services.Framework.CanvasWidth),
                ToCanvas(sp.Y, _services.Framework.ScreenHeight, _services.Framework.CanvasHeight));
        }
    }

    private static float ToCanvas(float px, int screen, int canvas) => screen > 0 ? px * canvas / screen : px;

    private void PlaceMarker(int i, float cx, float cy)
    {
        var w = _markers[i] ??= RegisterMarker(i);
        var (x, y) = (cx - MarkerW / 2f, cy - MarkerH / 2f);
        if (MathF.Abs(x - _markerAt[i].X) > MarkerMoveEpsilon || MathF.Abs(y - _markerAt[i].Y) > MarkerMoveEpsilon)
        {
            w.SetRect(new WindowRect(x, y, MarkerW, MarkerH));
            _markerAt[i] = (x, y);
        }
        if (!w.IsShown) w.SetVisible(true);
    }

    private void HideMarker(int i)
    {
        if (_markers[i] is { IsShown: true } w) w.SetVisible(false);
    }

    private IWindowControl RegisterMarker(int i)
    {
        var number = (i + 1).ToString(CultureInfo.InvariantCulture);
        return _services.Windows.Register(new WindowRegistration(
            new WindowSpec($"photostudio.lampmarker.{number}", T("lt.marker.title"), new WindowRect(0f, 0f, MarkerW, MarkerH),
                WindowCategory.HUD, WindowPanelStyle.Borderless)
            {
                Surface = SurfaceStyle.HudOverlay, ShowTitleBar = false, StartVisible = false, Draggable = false,
                Passive = true, Anchor = WindowAnchor.TopLeft, ShouldRender = MarkersWanted,
            },
            new RowElement(new HudElement[]
            {
                new SwatchElement(() => LampColor(i), Size: 14f),
                new PillElement(() => number, Color: () => _services.Theme.Colors.HudText),
            }, Gap: 3f)));
    }

    private void RemoveMarkers()
    {
        for (var i = 0; i < _markers.Length; i++)
        {
            _markers[i]?.Remove();
            _markers[i] = null;
        }
    }
}
