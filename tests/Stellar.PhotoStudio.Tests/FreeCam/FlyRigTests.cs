using System.Numerics;
using Stellar.PhotoStudio.FreeCam;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

public sealed class FlyRigTests
{
    private static readonly RigTuning Tune = new(4.5f, 1f, 0f, false, 30f);

    [Fact]
    public void Forward_flies_along_the_look_direction()
    {
        var s = FlyRig.Step(new FlyState(Vector3.Zero, 90f, 0f), new CamIntent { Move = new Vector3(0, 0, 1) }, Tune, Vector3.Zero, 1f);
        Assert.True(Vector3.Distance(new Vector3(4.5f, 0, 0), s.Position) < 1e-3f);
    }

    [Fact]
    public void Shift_is_three_times_and_ctrl_a_quarter()
    {
        var fwd = new CamIntent { Move = new Vector3(0, 0, 1), Shift = true };
        Assert.InRange(FlyRig.Step(new FlyState(Vector3.Zero, 0f, 0f), fwd, Tune, Vector3.Zero, 1f).Position.Z, 13.499f, 13.501f);
        var slow = new CamIntent { Move = new Vector3(0, 0, 1), Ctrl = true };
        Assert.InRange(FlyRig.Step(new FlyState(Vector3.Zero, 0f, 0f), slow, Tune, Vector3.Zero, 1f).Position.Z, 1.124f, 1.126f);
    }

    [Fact]
    public void Never_leaves_the_leash_and_slides_along_it()
    {
        var s = new FlyState(new Vector3(29, 0, 0), 0f, 0f);
        s = FlyRig.Step(s, new CamIntent { Move = new Vector3(1, 0, 0) }, Tune, Vector3.Zero, 1f);   // outward (+X)
        Assert.InRange(s.Position.Length(), 29.999f, 30.001f);
        var z = s.Position.Z;
        s = FlyRig.Step(s, new CamIntent { Move = new Vector3(0, 0, 1) }, Tune, Vector3.Zero, 1f);   // tangential (+Z)
        Assert.InRange(s.Position.Length(), 29.999f, 30.001f);
        Assert.True(s.Position.Z > z);
    }
}
