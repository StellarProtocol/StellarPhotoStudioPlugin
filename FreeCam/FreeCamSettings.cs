using System;
using System.Collections.Generic;
using System.Globalization;
using Stellar.Abstractions.Domain;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>The free camera's persisted choices (config section <c>photostudio</c>, keys <c>freecam.*</c>). Sliders save
/// via <see cref="SaveSliders"/> once a drag settles (a config save is a main-thread file write); toggles save at once.</summary>
internal sealed class FreeCamSettings
{
    internal const float DefaultSpeed = 4.5f, MinSpeed = 0.5f, MaxSpeed = 20f;
    internal const float DefaultSensitivity = 1f, MinSensitivity = 0.2f, MaxSensitivity = 3f;
    internal const float DefaultSmoothing = 0.3f;
    internal const float DefaultLeash = 30f, MinLeash = 5f, MaxLeash = 50f;

    /// <summary>Spec §§ 3/5: the layers entry may hide, and the default (game HUD + nameplates).</summary>
    internal const VisibilityLayers EntryHideChoices =
        VisibilityLayers.GameHud | VisibilityLayers.Nameplates | VisibilityLayers.OtherPlayers;
    internal const VisibilityLayers DefaultEntryHides = VisibilityLayers.GameHud | VisibilityLayers.Nameplates;
    private const string EntryHidesKey = "freecam.entryHideLayers";
    private const string LegacyEntryHidesKey = "freecam.entryHides";   // 1.1.0 dev builds: one bool over the default set

    private readonly IConfigSection _cfg;
    private readonly List<int> _favourites;

    public FreeCamSettings(IConfigSection cfg)
    {
        _cfg = cfg;
        MoveSpeed = Math.Clamp(cfg.Get("freecam.moveSpeed", DefaultSpeed), MinSpeed, MaxSpeed);
        Sensitivity = Math.Clamp(cfg.Get("freecam.sensitivity", DefaultSensitivity), MinSensitivity, MaxSensitivity);
        Smoothing = Math.Clamp(cfg.Get("freecam.smoothing", DefaultSmoothing), 0f, 1f);
        Leash = Math.Clamp(cfg.Get("freecam.leash", DefaultLeash), MinLeash, MaxLeash);
        InvertY = cfg.Get("freecam.invertY", false);
        EntryHides = LoadEntryHides(cfg);
        LookAt = cfg.Get("freecam.lookAt", false);
        HintHidden = cfg.Get("freecam.hintHidden", false);
        MovementOpen = cfg.Get("ui.freecam.movementOpen", true);
        PoseOpen = cfg.Get("ui.freecam.poseOpen", true);
        _favourites = ParseIds(cfg.Get("freecam.favourites", "") ?? "");
    }

    public float MoveSpeed { get; private set; }
    public float Sensitivity { get; private set; }
    public float Smoothing { get; private set; }
    public float Leash { get; private set; }
    public bool InvertY { get; private set; }
    /// <summary>The layers the free camera hides on entry (a subset of <see cref="EntryHideChoices"/>).</summary>
    public VisibilityLayers EntryHides { get; private set; }
    public bool LookAt { get; private set; }
    public bool HintHidden { get; private set; }
    public bool MovementOpen { get; private set; }
    public bool PoseOpen { get; private set; }
    public IReadOnlyList<int> Favourites => _favourites;
    public RigTuning Tuning => new(MoveSpeed, Sensitivity, Smoothing, InvertY, Leash);

    public void SetMoveSpeed(float v, bool save) { MoveSpeed = Math.Clamp(v, MinSpeed, MaxSpeed); if (save) SaveSliders(); }
    public void SetSensitivity(float v, bool save) { Sensitivity = Math.Clamp(v, MinSensitivity, MaxSensitivity); if (save) SaveSliders(); }
    public void SetSmoothing(float v, bool save) { Smoothing = Math.Clamp(v, 0f, 1f); if (save) SaveSliders(); }
    public void SetLeash(float v, bool save) { Leash = Math.Clamp(v, MinLeash, MaxLeash); if (save) SaveSliders(); }

    public void SaveSliders()
    {
        _cfg.Set("freecam.moveSpeed", MoveSpeed);
        _cfg.Set("freecam.sensitivity", Sensitivity);
        _cfg.Set("freecam.smoothing", Smoothing);
        _cfg.Set("freecam.leash", Leash);
        _cfg.SaveQuiet();
    }

    public void SetInvertY(bool on) { InvertY = on; Store("freecam.invertY", on); }
    public bool EntryHidesLayer(VisibilityLayers layer) => (EntryHides & layer) == layer;

    /// <summary>Adds or removes one layer of <see cref="EntryHideChoices"/>; the old bool key is never touched.</summary>
    public void SetEntryHide(VisibilityLayers layer, bool on)
    {
        var next = on ? EntryHides | layer : EntryHides & ~layer;
        EntryHides = next & EntryHideChoices;
        Store(EntryHidesKey, (int)EntryHides);
    }
    public void SetLookAt(bool on) { LookAt = on; Store("freecam.lookAt", on); }
    public void SetHintHidden(bool on) { HintHidden = on; Store("freecam.hintHidden", on); }
    public void SetMovementOpen(bool open) { MovementOpen = open; Store("ui.freecam.movementOpen", open); }
    public void SetPoseOpen(bool open) { PoseOpen = open; Store("ui.freecam.poseOpen", open); }

    public bool IsFavourite(int id) => _favourites.Contains(id);

    public void ToggleFavourite(int id)
    {
        if (!_favourites.Remove(id)) _favourites.Add(id);
        Store("freecam.favourites", string.Join(",", _favourites));
    }

    private void Store<T>(string key, T value)
    {
        _cfg.Set(key, value);
        _cfg.SaveQuiet();
    }

    /// <summary>The flags key when saved; otherwise the old bool migrates (false → none, true → the default).</summary>
    private static VisibilityLayers LoadEntryHides(IConfigSection cfg)
    {
        var saved = cfg.Get(EntryHidesKey, -1);
        if (saved >= 0) return (VisibilityLayers)saved & EntryHideChoices;
        return cfg.Get(LegacyEntryHidesKey, true) ? DefaultEntryHides : VisibilityLayers.None;
    }

    private static List<int> ParseIds(string csv)
    {
        var ids = new List<int>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && !ids.Contains(id)) ids.Add(id);
        return ids;
    }
}
