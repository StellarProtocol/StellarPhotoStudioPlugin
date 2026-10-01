using System.Numerics;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio.Tests.Posing;
using Xunit;

namespace Stellar.PhotoStudio.Tests.FreeCam;

// Controller decision Q5 (2026-10-02): while a person is posed as a copy / NPC stand-in, the free camera orbits what is
// visible — the copy — not the hidden real person, who may walk away.
public sealed class FreeCamSessionPosingTests
{
    private static float Dist(Position3D a, Vector3 b) => Vector3.Distance(new Vector3(a.X, a.Y, a.Z), b);

    private static SessionRig Orbiting(FakePosing posing)
    {
        var r = new SessionRig(posing: posing);
        r.Settings.SetLeash(5f, save: false);
        r.Transforms.Positions[2] = new Position3D(3, 0, 3);
        r.Session.Enter();
        r.Session.SetSubject(new EntityId(2));
        return r;
    }

    [Fact]
    public void The_orbit_follows_the_visible_copy_not_the_hidden_player()
    {
        var posing = new FakePosing();
        posing.Visible[2] = new Position3D(3, 0, 3);
        var r = Orbiting(posing);
        r.Transforms.Positions[2] = new Position3D(100, 0, 0);   // the hidden real player walks off
        for (var i = 0; i < 60; i++) r.Frame();
        Assert.True(Dist(r.Camera.Control.LastPosition, new Vector3(3, 0, 3)) <= 5.001f);
        Assert.Equal(new EntityId(2), r.Session.Subject);
    }

    [Fact]
    public void Without_a_posed_copy_the_orbit_follows_the_person()
    {
        var r = Orbiting(new FakePosing());
        r.Transforms.Positions[2] = new Position3D(100, 0, 0);
        for (var i = 0; i < 60; i++) r.Frame();
        Assert.True(Dist(r.Camera.Control.LastPosition, new Vector3(100, 0, 0)) <= 5.001f);
    }
}
