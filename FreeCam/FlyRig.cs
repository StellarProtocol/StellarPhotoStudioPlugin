using System.Numerics;

namespace Stellar.PhotoStudio.FreeCam;

internal readonly record struct FlyState(Vector3 Position, float Yaw, float Pitch);

/// <summary>Fly mode (spec § 3): WASD along the view, Q/E down/up, RMB mouse-look; projected into the leash sphere.</summary>
internal static class FlyRig
{
    internal static FlyState Step(FlyState s, in CamIntent i, RigTuning t, Vector3 leashCentre, float dt)
    {
        var (yaw, pitch) = OrbitRig.Look(s.Yaw, s.Pitch, i, t);
        var step = t.MoveSpeed * CameraMath.SpeedFactor(i.Shift, i.Ctrl) * dt;
        var move = CameraMath.Right(yaw) * i.Move.X + Vector3.UnitY * i.Move.Y + CameraMath.Forward(yaw, pitch) * i.Move.Z;
        return new FlyState(CameraMath.ProjectIntoSphere(s.Position + move * step, leashCentre, t.Leash), yaw, pitch);
    }
}
