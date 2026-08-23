namespace Lockstep;

/// <summary>
/// In-memory transport pair for tests and local tooling. Delivery is delayed by a
/// whole number of receiver pumps per direction, which makes latency itself
/// deterministic — no wall clock anywhere.
/// </summary>
public sealed class LoopbackTransport : ITransport
{
    private readonly Queue<byte[]> _inbound = new();
    private readonly List<(int deliverAtPump, byte[] payload)> _delayed = new();
    private LoopbackTransport? _peer;
    private readonly int _delayPumps;
    private int _pumps;

    private LoopbackTransport(int delayPumps, LoopbackTransport? peer = null)
    {
        _delayPumps = delayPumps;
        _peer = peer;
    }

    /// <summary>Create two linked transports with symmetric delay (0 = instant).</summary>
    public static (LoopbackTransport a, LoopbackTransport b) CreatePair(int delayPumpsEachWay = 0)
    {
        var b = new LoopbackTransport(delayPumpsEachWay);
        var a = new LoopbackTransport(delayPumpsEachWay, b);
        b.AttachPeer(a);
        return (a, b);
    }

    private void AttachPeer(LoopbackTransport peer) => _peer = peer;

    public void Send(ReadOnlySpan<byte> payload)
    {
        if (_peer is null)
            throw new InvalidOperationException("LoopbackTransport without a peer cannot send");
        var copy = payload.ToArray();
        // Available on the peer's (pumps + delay + 1)-th receive call.
        // delay 0 therefore means "the very next time you look".
        _peer._delayed.Add((_peer._pumps + _delayPumps + 1, copy));
    }

    public int Receive(Span<byte> destination)
    {
        _pumps++;
        // Release strictly in send order (FIFO) among packets due this pump.
        _delayed.Sort((x, y) => x.deliverAtPump.CompareTo(y.deliverAtPump));
        for (int i = 0; i < _delayed.Count && _delayed.Count > 0; )
        {
            if (_delayed[i].deliverAtPump <= _pumps)
            {
                _inbound.Enqueue(_delayed[i].payload);
                _delayed.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }

        if (_inbound.Count == 0)
            return 0;

        var packet = _inbound.Dequeue();
        if (packet.Length > destination.Length)
            throw new BufferTooSmallException(packet.Length);
        packet.CopyTo(destination);
        return packet.Length;
    }
}

public sealed class BufferTooSmallException : Exception
{
    public int RequiredSize { get; }
    public BufferTooSmallException(int required) : base($"Receive buffer too small: needs {required} bytes")
        => RequiredSize = required;
}
