using Lockstep;
using Xunit;

using Sim;

namespace Lockstep.Tests;

public class ProtocolTests
{
    [Fact]
    public void Hello_RoundTrips()
    {
        var hello = new Protocol.Handshake(Seed: 123456789, MapDigest: 0xDEADBEEFCAFE, InputDelayTicks: 5, WillAdoptSeed: false, FactionAllies: 0, FactionCentral: 2);
        var bytes = Protocol.EncodeHello(hello);

        var ok = Protocol.TryDecodeHello(bytes, out var decoded, out var error);

        Assert.True(ok, error);
        Assert.Equal(hello.Seed, decoded.Seed);
        Assert.Equal(hello.MapDigest, decoded.MapDigest);
        Assert.Equal(hello.InputDelayTicks, decoded.InputDelayTicks);
    }

    [Fact]
    public void Hello_RejectsWrongVersion()
    {
        var bytes = Protocol.EncodeHello(new Protocol.Handshake(1, 2, 3, false, 0, 2));
        bytes[1] = 0xFF; // clobber version
        // Recompute length is unchanged; decoder must reject on version.
        var ok = Protocol.TryDecodeHello(bytes, out _, out var error);
        Assert.False(ok);
        Assert.Contains("version", error);
    }

    [Fact]
    public void Frame_RoundTripsCommands()
    {
        var commands = new List<Command>
        {
            new(3, CommandType.AttackMove, new Fixed2(Fixed.FromInt(12), Fixed.FromRatio(-34, 100))),
            new(7, CommandType.Move, new Fixed2(Fixed.FromRaw(long.MaxValue), Fixed.Zero)),
            new(0, CommandType.Stop, Fixed2.Zero),
            new(9, CommandType.Barrage,
                new Fixed2(Fixed.FromInt(40), Fixed.FromInt(32)),
                new Fixed2(Fixed.FromInt(55), Fixed.FromInt(32))),
            new(4, CommandType.Dig, new Fixed2(Fixed.FromInt(8), Fixed.FromInt(8))),
            new(2, CommandType.Requisition, Fixed2.Zero, default, 7),
        };
        var bytes = Protocol.EncodeFrame(tick: 424242, commands);

        var ok = Protocol.TryDecodeFrame(bytes, out int tick, out var decoded, out var error);

        Assert.True(ok, error);
        Assert.Equal(424242, tick);
        Assert.Equal(commands.Count, decoded.Count);
        for (int i = 0; i < commands.Count; i++)
        {
            Assert.Equal(commands[i].UnitId, decoded[i].UnitId);
            Assert.Equal(commands[i].Type, decoded[i].Type);
            Assert.Equal(commands[i].Pos.X.Raw, decoded[i].Pos.X.Raw);
            Assert.Equal(commands[i].Pos.Y.Raw, decoded[i].Pos.Y.Raw);
        }
    }

    [Fact]
    public void Frame_RejectsTruncatedPayload()
    {
        var bytes = Protocol.EncodeFrame(1, new List<Command> { new(0, CommandType.Stop, Fixed2.Zero) });
        var truncated = bytes.AsSpan(0, bytes.Length - 5).ToArray();
        var ok = Protocol.TryDecodeFrame(truncated, out _, out _, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void HashReport_RoundTrips()
    {
        var bytes = Protocol.EncodeHashReport(tick: -7, stateHash: ulong.MaxValue);
        var (tick, hash) = Protocol.DecodeHashReport(bytes);
        Assert.Equal(-7, tick);
        Assert.Equal(ulong.MaxValue, hash);
    }
}

public class CanonicalMergeTests
{
    private static Command C(int unit, CommandType t, long x) =>
        new(unit, t, new Fixed2(Fixed.FromRaw(x), Fixed.Zero));

    [Fact]
    public void Merge_IsOrderIndependent()
    {
        var mine = new List<Command> { C(5, CommandType.Move, 10), C(1, CommandType.Stop, 0) };
        var theirs = new List<Command> { C(9, CommandType.AttackMove, 30), C(1, CommandType.Move, 99) };

        var ab = LockstepSession.CanonicalMerge(mine, theirs);
        var ba = LockstepSession.CanonicalMerge(theirs, mine);

        Assert.Equal(ab.Count, ba.Count);
        for (int i = 0; i < ab.Count; i++)
        {
            Assert.Equal(ab[i].UnitId, ba[i].UnitId);
            Assert.Equal(ab[i].Type, ba[i].Type);
            Assert.Equal(ab[i].Pos.X.Raw, ba[i].Pos.X.Raw);
        }
        // And it is actually sorted by unit id.
        for (int i = 1; i < ab.Count; i++)
            Assert.True(ab[i - 1].UnitId <= ab[i].UnitId);
    }
}

public class MapDigestTests
{
    [Fact]
    public void IdenticalMaps_SameDigest()
    {
        Assert.Equal(MapDigest.Of(Harness.Map()), MapDigest.Of(Harness.Map()));
    }

    [Fact]
    public void ChangedMap_DifferentDigest()
    {
        var widened = Harness.Map() with { Width = Harness.M(97) };
        Assert.NotEqual(MapDigest.Of(Harness.Map()), MapDigest.Of(widened));
    }

    [Fact]
    public void ReorderedPoints_DifferentDigest()
    {
        var original = Harness.Map() with
        {
            CapturePoints = new[]
            {
                new CapturePointSpec(new Fixed2(Harness.M(48), Harness.M(32)), Harness.M(6), IsVictoryPoint: true),
                new CapturePointSpec(new Fixed2(Harness.M(24), Harness.M(16)), Harness.M(6), IsVictoryPoint: false),
                new CapturePointSpec(new Fixed2(Harness.M(72), Harness.M(48)), Harness.M(6), IsVictoryPoint: false),
            },
        };
        var reordered = original with { CapturePoints = original.CapturePoints.Reverse().ToList() };

        Assert.NotEqual(MapDigest.Of(original), MapDigest.Of(reordered));
    }
}
