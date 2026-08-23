namespace Lockstep;

/// <summary>
/// A reliable, ordered, single-peer channel. Implementations must deliver every
/// payload exactly once and in send order — the lockstep protocol does not
/// retransmit or reorder. (Steam networking sockets in reliable mode, QUIC
/// streams, TCP, or an in-memory pipe all satisfy this.)
/// Not thread-safe by contract: pump it from one place (the game's sim thread).
/// </summary>
public interface ITransport
{
    /// <summary>Bytes available from the peer, written into <paramref name="destination"/>.
    /// Returns 0 when nothing is waiting. The caller retries with a buffer of at least
    /// <see cref="MaxPacketSize"/> bytes.</summary>
    int Receive(Span<byte> destination);

    /// <summary>Queue a payload for the peer.</summary>
    void Send(ReadOnlySpan<byte> payload);

    /// <summary>Upper bound on any single protocol packet. Receive buffers must be at least this large.</summary>
    const int MaxPacketSize = 8192;
}
