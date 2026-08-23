using Sim;

namespace Lockstep;

public static class Protocol
{
    /// <summary>Bump when the packet format or semantics change. Peers refuse mismatched versions.</summary>
    public const ushort Version = 1;

    public enum PacketType : byte
    {
        Hello = 1,
        InputFrame = 2,
        HashReport = 3,
    }

    // --- Hello ---

    /// <summary>WillAdoptSeed: sender agrees to rebuild its world from the peer's seed if they differ.</summary>
    public sealed record Handshake(ulong Seed, ulong MapDigest, byte InputDelayTicks, bool WillAdoptSeed);

    public static byte[] EncodeHello(in Handshake handshake)
    {
        using var ms = new MemoryStream(32);
        using var w = new BinaryWriter(ms);
        w.Write((byte)PacketType.Hello);
        w.Write(Protocol.Version);
        w.Write(handshake.Seed);
        w.Write(handshake.MapDigest);
        w.Write(handshake.InputDelayTicks);
        w.Write(handshake.WillAdoptSeed);
        w.Flush();
        return ms.ToArray();
    }

    public static bool TryDecodeHello(ReadOnlySpan<byte> payload, out Handshake handshake, out string? error)
    {
        handshake = new Handshake(0, 0, 0, false);
        error = null;
        if (payload.Length != 1 + 2 + 8 + 8 + 1 + 1)
        {
            error = $"hello size {payload.Length}";
            return false;
        }

        var reader = new BinaryReader(new MemoryStream(payload.ToArray()));
        reader.ReadByte(); // type already dispatched by caller
        ushort version = reader.ReadUInt16();
        if (version != Protocol.Version)
        {
            error = $"protocol version {version}, expected {Protocol.Version}";
            return false;
        }

        ulong seed = reader.ReadUInt64();
        ulong mapDigest = reader.ReadUInt64();
        byte delay = reader.ReadByte();
        bool willAdopt = reader.ReadBoolean();
        handshake = new Handshake(seed, mapDigest, delay, willAdopt);
        return true;
    }

    // --- InputFrame ---

    public static byte[] EncodeFrame(int tick, IReadOnlyList<Command> commands)
    {
        using var ms = new MemoryStream(16 + commands.Count * 21);
        using var w = new BinaryWriter(ms);
        w.Write((byte)PacketType.InputFrame);
        w.Write(tick);
        w.Write((ushort)commands.Count);
        foreach (var c in commands)
            WriteCommand(w, c);
        w.Flush();
        return ms.ToArray();
    }

    public static bool TryDecodeFrame(ReadOnlySpan<byte> payload, out int tick, out List<Command> commands, out string? error)
    {
        tick = 0;
        commands = new List<Command>();
        error = null;

        if (payload.Length < 7)
        {
            error = "frame truncated";
            return false;
        }

        var reader = new BinaryReader(new MemoryStream(payload.ToArray()));
        reader.ReadByte(); // type
        tick = reader.ReadInt32();
        int count = reader.ReadUInt16();

        try
        {
            for (int i = 0; i < count; i++)
                commands.Add(ReadCommand(reader));
        }
        catch (EndOfStreamException)
        {
            error = "frame command list truncated";
            commands.Clear();
            return false;
        }
        return true;
    }

    private static void WriteCommand(BinaryWriter w, in Command c)
    {
        w.Write(c.UnitId);
        w.Write((byte)c.Type);
        w.Write(c.Pos.X.Raw);
        w.Write(c.Pos.Y.Raw);
    }

    private static Command ReadCommand(BinaryReader r) => new(
        r.ReadInt32(),
        (CommandType)r.ReadByte(),
        new Fixed2(Fixed.FromRaw(r.ReadInt64()), Fixed.FromRaw(r.ReadInt64())));

    // --- HashReport ---

    public static byte[] EncodeHashReport(int tick, ulong stateHash)
    {
        var buf = new byte[13];
        buf[0] = (byte)PacketType.HashReport;
        BitConverter.GetBytes(tick).CopyTo(buf, 1);
        BitConverter.GetBytes(stateHash).CopyTo(buf, 5);
        return buf;
    }

    public static (int tick, ulong hash) DecodeHashReport(ReadOnlySpan<byte> payload)
    {
        int tick = BitConverter.ToInt32(payload.Slice(1, 4));
        ulong hash = BitConverter.ToUInt64(payload.Slice(5, 8));
        return (tick, hash);
    }
}
