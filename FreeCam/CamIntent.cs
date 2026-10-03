using System.Numerics;

namespace Stellar.PhotoStudio.FreeCam;

/// <summary>One frame of camera input, already mapped from keys and mouse (<see cref="FreeCamInput"/>).</summary>
internal readonly record struct CamIntent
{
    /// <summary>X = right (D − A), Y = up (E − Q), Z = forward (W − S); each −1..1.</summary>
    public Vector3 Move { get; init; }
    /// <summary>Pointer movement this frame, pixels, right positive.</summary>
    public float LookX { get; init; }
    /// <summary>Pointer movement this frame, pixels, down positive.</summary>
    public float LookY { get; init; }
    /// <summary>Right mouse button held (rotate / mouse-look).</summary>
    public bool Looking { get; init; }
    /// <summary>Wheel notches this frame, up positive.</summary>
    public float Wheel { get; init; }
    /// <summary>−1 (Z) .. +1 (C).</summary>
    public float RollAxis { get; init; }
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
}

/// <summary>The player's movement settings as the rigs need them.</summary>
internal readonly record struct RigTuning(float MoveSpeed, float Sensitivity, float Smoothing, bool InvertY, float Leash);
