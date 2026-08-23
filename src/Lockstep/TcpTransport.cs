using System.Net;
using System.Net.Sockets;

namespace Lockstep;

/// <summary>
/// Plain-TCP transport: reliable and ordered by nature, framed with a ushort
/// length prefix on the wire. Host listens for exactly one peer; join connects.
/// All sockets are non-blocking and pumped from the caller's thread (the game's
/// sim thread), so nothing ever stalls the frame loop. Works unchanged over
/// LAN or Tailscale IPs.
/// </summary>
public sealed class TcpTransport : ITransport, IDisposable
{
    private Socket? _listener;                 // host mode only
    private Socket? _socket;                   // the established peer connection
    private Task? _connectTask;                // join mode connect-in-progress

    private readonly List<byte> _recvBuffer = new();
    private readonly Queue<byte[]> _outbox = new();
    private int _outboxOffset;
    private readonly byte[] _readChunk = new byte[16 * 1024];

    public bool Connected { get; private set; }
    public bool Failed { get; private set; }
    public string? LastError { get; private set; }

    private TcpTransport() { }

    /// <summary>Begin listening. Call <see cref="Pump"/> each frame until Connected.</summary>
    public static TcpTransport Listen(int port)
    {
        var t = new TcpTransport();
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            Blocking = false,
            NoDelay = true,
        };
        listener.Bind(new IPEndPoint(IPAddress.Any, port));
        listener.Listen(1);
        t._listener = listener;
        return t;
    }

    /// <summary>Begin connecting asynchronously. Call <see cref="Pump"/> each frame until Connected or Failed.</summary>
    public static TcpTransport Connect(string host, int port)
    {
        var t = new TcpTransport();
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
        t._socket = socket;
        t._connectTask = socket.ConnectAsync(host, port);
        return t;
    }

    /// <summary>
    /// Progress connection state: accept an inbound peer (host), complete a pending
    /// connect (join), flush queued output, and detect a closed link. Call once per frame.
    /// </summary>
    public void Pump()
    {
        if (Failed)
            return;

        try
        {
            if (_connectTask is { IsCompleted: true })
            {
                var task = _connectTask;
                _connectTask = null;
                if (task.Exception is not null)
                    throw task.Exception.GetBaseException();
                FinishConnect();
            }

            if (_socket is null && _listener is not null)
            {
                TryAccept();
            }

            if (Connected)
            {
                DetectClosure();
                FlushOutbox();
            }
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            Fail(e.Message);
        }
    }

    private void TryAccept()
    {
        try
        {
            var accepted = _listener!.Accept();
            Configure(accepted);
            _socket = accepted;
            Connected = true;
        }
        catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
        {
            // Nobody at the door yet.
        }
    }

    private void FinishConnect()
    {
        Configure(_socket!);
        // ConnectAsync leaves the socket blocking; make it non-blocking for reads/writes.
        Connected = true;
    }

    private void DetectClosure()
    {
        var s = _socket!;
        bool readable = s.Poll(0, SelectMode.SelectRead);
        if (readable && s.Available == 0 && _recvBuffer.Count < 2)
        {
            Fail("peer disconnected");
        }
    }

    private static void Configure(Socket s)
    {
        s.Blocking = false;
        s.NoDelay = true;
    }

    private void Fail(string reason)
    {
        Failed = true;
        LastError = reason;
        Connected = false;
        try { _socket?.Dispose(); } catch { /* best effort */ }
        try { _listener?.Dispose(); } catch { /* best effort */ }
        _socket = null;
        _listener = null;
    }

    public void Send(ReadOnlySpan<byte> payload)
    {
        if (!Connected || Failed || payload.Length > ITransport.MaxPacketSize - 2)
            return;

        var framed = new byte[payload.Length + 2];
        framed[0] = (byte)(payload.Length & 0xFF);
        framed[1] = (byte)(payload.Length >> 8);
        payload.CopyTo(framed.AsSpan(2));

        _outbox.Enqueue(framed);
        FlushOutbox();
    }

    private void FlushOutbox()
    {
        var s = _socket;
        if (s is null || !Connected)
            return;

        while (_outbox.Count > 0)
        {
            var packet = _outbox.Peek();
            try
            {
                int sent = s.Send(packet, _outboxOffset, packet.Length - _outboxOffset, SocketFlags.None);
                _outboxOffset += sent;
                if (_outboxOffset == packet.Length)
                {
                    _outbox.Dequeue();
                    _outboxOffset = 0;
                }
                else
                {
                    return; // socket buffer full; resume next pump
                }
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
            {
                return;
            }
        }
    }

    public int Receive(Span<byte> destination)
    {
        if (!Connected || Failed)
            return 0;

        DrainSocket();

        // Extract one complete length-prefixed packet, if present.
        while (_recvBuffer.Count >= 2)
        {
            int len = _recvBuffer[0] | (_recvBuffer[1] << 8);
            if (len == 0 || len > ITransport.MaxPacketSize - 2)
            {
                Fail($"bad frame length {len}");
                return 0;
            }
            if (_recvBuffer.Count < 2 + len)
                return 0; // wait for the rest

            _recvBuffer.RemoveRange(0, 2);
            var packet = _recvBuffer.GetRange(0, len).ToArray();
            _recvBuffer.RemoveRange(0, len);

            if (packet.Length <= destination.Length)
            {
                packet.CopyTo(destination);
                return packet.Length;
            }
            throw new BufferTooSmallException(packet.Length);
        }

        return 0;
    }

    private void DrainSocket()
    {
        var s = _socket;
        if (s is null || !Connected)
            return;

        while (true)
        {
            try
            {
                if (!s.Poll(0, SelectMode.SelectRead))
                    return;
                if (s.Available == 0)
                {
                    // Poll readable with zero available means orderly closure,
                    // unless we still hold buffered bytes to consume first.
                    if (_recvBuffer.Count < 2)
                        Fail("peer disconnected");
                    return;
                }

                int read = s.Receive(_readChunk, 0, _readChunk.Length, SocketFlags.None);
                if (read > 0)
                    _recvBuffer.AddRange(_readChunk.AsSpan(0, read).ToArray());
                if (read == 0)
                {
                    Fail("peer disconnected");
                    return;
                }
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        try { _socket?.Dispose(); } catch { /* best effort */ }
        try { _listener?.Dispose(); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }
}
