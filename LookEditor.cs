using System;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

/// <summary>
/// The look being edited in the panel. Every group always keeps its last values, so switching a group off and on
/// again restores what the player had instead of resetting it; <see cref="Build"/> emits null for groups that are
/// off, which the framework reads as "leave the game's own value".
/// </summary>
internal sealed class LookEditor
{
    public DofLook Dof { get; private set; } = new();
    public ColorLook Color { get; private set; } = new();
    public WhiteBalanceLook WhiteBalance { get; private set; } = new();
    public LutLook Lut { get; private set; } = new() { FilePath = "" };
    public BloomLook Bloom { get; private set; } = new();
    public VignetteLook Vignette { get; private set; } = new();
    public FilmGrainLook FilmGrain { get; private set; } = new();

    /// <summary>Which groups are on.</summary>
    public LookGroups Enabled { get; private set; }

    /// <summary>Raised after any change, so the owner can push the new look and mark the preset modified.</summary>
    public event Action? Changed;

    public bool IsOn(LookGroups g) => (Enabled & g) != 0;

    public void SetOn(LookGroups g, bool on)
    {
        var next = on ? Enabled | g : Enabled & ~g;
        if (next == Enabled) return;
        Enabled = next;
        Changed?.Invoke();
    }

    // Each setter turns its group on: moving a slider means "I want this effect".
    public void EditDof(Func<DofLook, DofLook> f) { Dof = f(Dof); Touch(LookGroups.Dof); }
    public void EditColor(Func<ColorLook, ColorLook> f) { Color = f(Color); Touch(LookGroups.Color); }
    public void EditWhiteBalance(Func<WhiteBalanceLook, WhiteBalanceLook> f) { WhiteBalance = f(WhiteBalance); Touch(LookGroups.WhiteBalance); }
    public void EditLut(Func<LutLook, LutLook> f) { Lut = f(Lut); Touch(LookGroups.Lut); }
    public void EditBloom(Func<BloomLook, BloomLook> f) { Bloom = f(Bloom); Touch(LookGroups.Bloom); }
    public void EditVignette(Func<VignetteLook, VignetteLook> f) { Vignette = f(Vignette); Touch(LookGroups.Vignette); }
    public void EditFilmGrain(Func<FilmGrainLook, FilmGrainLook> f) { FilmGrain = f(FilmGrain); Touch(LookGroups.FilmGrain); }

    /// <summary>Resets one group's values to their neutral defaults (the ↺ button); keeps it on.</summary>
    public void ResetGroup(LookGroups g)
    {
        switch (g)
        {
            case LookGroups.Dof: Dof = new DofLook { FocusOnLocalPlayer = Dof.FocusOnLocalPlayer }; break;
            case LookGroups.Color: Color = new ColorLook(); break;
            case LookGroups.WhiteBalance: WhiteBalance = new WhiteBalanceLook(); break;
            case LookGroups.Lut: Lut = Lut with { Contribution = 1f }; break;
            case LookGroups.Bloom: Bloom = new BloomLook(); break;
            case LookGroups.Vignette: Vignette = new VignetteLook(); break;
            case LookGroups.FilmGrain: FilmGrain = new FilmGrainLook(); break;
        }
        Changed?.Invoke();
    }

    /// <summary>Loads a preset: groups present in it are switched on with its values, the rest off
    /// (their remembered values are reset to defaults so a new preset starts clean).</summary>
    public void Load(LookSettings s)
    {
        Dof = s.Dof ?? new DofLook();
        Color = s.Color ?? new ColorLook();
        WhiteBalance = s.WhiteBalance ?? new WhiteBalanceLook();
        Lut = s.Lut ?? new LutLook { FilePath = "" };
        Bloom = s.Bloom ?? new BloomLook();
        Vignette = s.Vignette ?? new VignetteLook();
        FilmGrain = s.FilmGrain ?? new FilmGrainLook();
        Enabled = Groups(s);
        Changed?.Invoke();
    }

    /// <summary>The look to apply: null for every group that is off. A LUT with no file counts as off.</summary>
    public LookSettings Build() => new()
    {
        Dof = IsOn(LookGroups.Dof) ? Dof : null,
        Color = IsOn(LookGroups.Color) ? Color : null,
        WhiteBalance = IsOn(LookGroups.WhiteBalance) ? WhiteBalance : null,
        Lut = IsOn(LookGroups.Lut) && Lut.FilePath.Length > 0 ? Lut : null,
        Bloom = IsOn(LookGroups.Bloom) ? Bloom : null,
        Vignette = IsOn(LookGroups.Vignette) ? Vignette : null,
        FilmGrain = IsOn(LookGroups.FilmGrain) ? FilmGrain : null,
    };

    private void Touch(LookGroups g)
    {
        Enabled |= g;
        Changed?.Invoke();
    }

    private static LookGroups Groups(LookSettings s)
    {
        var g = LookGroups.None;
        if (s.Dof is not null) g |= LookGroups.Dof;
        if (s.Color is not null) g |= LookGroups.Color;
        if (s.WhiteBalance is not null) g |= LookGroups.WhiteBalance;
        if (s.Lut is not null) g |= LookGroups.Lut;
        if (s.Bloom is not null) g |= LookGroups.Bloom;
        if (s.Vignette is not null) g |= LookGroups.Vignette;
        if (s.FilmGrain is not null) g |= LookGroups.FilmGrain;
        return g;
    }
}
