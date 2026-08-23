using Sim;

namespace Lockstep;

public enum SessionStatus
{
    /// <summary>Constructed but Start not called.</summary>
    Created,
    /// <summary>Hello exchange in flight.</summary>
    AwaitingHandshake,
    /// <summary>Simulating; inputs exchanged, hashes compared every tick.</summary>
    Running,
    /// <summary>A state-hash mismatch was detected. The session freezes here.</summary>
    Desynced,
    /// <summary>Protocol violation or incompatible peer. The session refuses to run.</summary>
    Faulted,
}

public sealed record DesyncReport(int Tick, ulong LocalHash, ulong RemoteHash);

/// <summary>
/// Drives one peer of a two-player deterministic lockstep match over a reliable,
/// ordered transport. Both peers run identical World instances; player input is
/// exchanged a fixed number of ticks ahead (input delay) so execution never stalls
/// behind the network, and every executed tick's state hash is cross-checked.
///
/// Pacing model (no wall clock): after executing tick L, this peer publishes its
/// input frames for all slots up to L + 1 + InputDelayTicks. A tick executes once
/// both peers' frames for it exist. Progress therefore advances exactly as fast
/// as the slower peer pumps its transport.
/// </summary>
public sealed class LockstepSession
{
    private World _world;
    private readonly ITransport _transport;
    private readonly byte _inputDelayTicks;
    private Sim.MatchOptions _options;

    /// <summary>
    /// When true, a seed mismatch during the handshake rebuilds this peer's world with the
    /// HOST's seed instead of faulting. The joiner sets this; the host never does, so exactly
    /// one side adopts and the other's seed wins. Requires all starting forces to live in the
    /// map definition (they do).
    /// </summary>
    public bool AdoptPeerSeed { get; set; }

    /// <summary>Raised when <see cref="AdoptPeerSeed"/> replaced the world. Views must rebind.</summary>
    public event Action<World>? WorldReplaced;

    // Frames not yet consumed by execution, indexed by target tick.
    private readonly Dictionary<int, List<Command>> _myFrames = new();
    private readonly Dictionary<int, List<Command>> _peerFrames = new();

    // State hashes of executed ticks (mine and peer-reported), for desync checks.
    private readonly Dictionary<int, ulong> _myHashes = new();
    private readonly Dictionary<int, ulong> _peerHashes = new();

    // Raw packets received before the handshake completed.
    private readonly List<byte[]> _earlyPackets = new();

    // Local commands queued for future outbound frames.
    private readonly Queue<Command> _localOutbox = new();

    private readonly byte[] _receiveBuffer = new byte[ITransport.MaxPacketSize];

    private int _nextFrameToSend;   // next outbound slot index (1-based)
    private int _lastExecutedTick;  // == _world.Tick

    public SessionStatus Status { get; private set; } = SessionStatus.Created;
    public string? FaultReason { get; private set; }
    public DesyncReport? Desync { get; private set; }

    public int ExecutedTick => _lastExecutedTick;
    public byte InputDelayTicks => _inputDelayTicks;
    /// <summary>The world being simulated. May be replaced by seed adoption; listen for WorldReplaced.</summary>
    public World World => _world;
    internal int OutboundQueueLength => _nextFrameToSend - 1 - _lastExecutedTick;

    public LockstepSession(World world, ITransport transport, byte inputDelayTicks = 3,
        Sim.MatchOptions? options = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _inputDelayTicks = inputDelayTicks;
        _options = options ?? world.Options;
        _nextFrameToSend = 1;
        _lastExecutedTick = world.Tick; // 0 for a fresh world
    }

    public void Start()
    {
        if (Status != SessionStatus.Created)
            throw new InvalidOperationException($"Start called in state {Status}");
        Status = SessionStatus.AwaitingHandshake;
        var hello = Protocol.EncodeHello(new Protocol.Handshake(
            _world.MatchSeed,
            MapDigest.Of(_world.Map),
            _inputDelayTicks,
            AdoptPeerSeed,
            (byte)_options.FactionAllies,
            (byte)_options.FactionCentral));
        _transport.Send(hello);
    }

    /// <summary>
    /// Queue local player commands. They ride the next outbound input frame and will
    /// execute roughly InputDelayTicks from now. Safe to call between Updates.
    /// </summary>
    public void EnqueueLocal(IReadOnlyList<Command> commands)
    {
        foreach (var c in commands)
            _localOutbox.Enqueue(c);
    }

    /// <summary>Pump the transport once: receive packets, publish due frames, execute eligible ticks.</summary>
    public void Update()
    {
        if (Status is SessionStatus.Desynced or SessionStatus.Faulted)
            return;

        ReceivePump();

        if (Status == SessionStatus.AwaitingHandshake)
            return;

        PublishDueFrames();
        ExecuteEligibleTicks();
    }

    // --- receiving ---

    private void ReceivePump()
    {
        int n;
        while ((n = _transport.Receive(_receiveBuffer)) > 0)
        {
            HandlePacket(_receiveBuffer.AsSpan(0, n));
            if (Status is SessionStatus.Desynced or SessionStatus.Faulted)
                return;
        }
    }

    private void HandlePacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 1)
        {
            Fault("empty packet");
            return;
        }

        var type = (Protocol.PacketType)packet[0];
        switch (type)
        {
            case Protocol.PacketType.Hello:
                HandleHello(packet);
                break;
            case Protocol.PacketType.InputFrame:
                if (Status == SessionStatus.AwaitingHandshake)
                    BufferEarly(packet);
                else
                    HandleInputFrame(packet);
                break;
            case Protocol.PacketType.HashReport:
                if (Status == SessionStatus.AwaitingHandshake)
                    BufferEarly(packet);
                else
                    HandleHashReport(packet);
                break;
            default:
                Fault($"unknown packet type {type}");
                break;
        }
    }

    private void BufferEarly(ReadOnlySpan<byte> packet) => _earlyPackets.Add(packet.ToArray());

    private void HandleHello(ReadOnlySpan<byte> packet)
    {
        if (!Protocol.TryDecodeHello(packet, out var handshake, out var error))
        {
            Fault($"bad hello: {error}");
            return;
        }

        bool seedMismatch = handshake.Seed != _world.MatchSeed;
        if (seedMismatch)
        {
            // Exactly one side may adopt: the peer that declared WillAdoptSeed rebuilds
            // its world from the other's seed AND the other's factions; the other side
            // simply tolerates the offer.
            if (AdoptPeerSeed)
            {
                _options = _options with
                {
                    FactionAllies = (Sim.FactionId)handshake.FactionAllies,
                    FactionCentral = (Sim.FactionId)handshake.FactionCentral,
                };
                _world = new World(_world.Map, handshake.Seed, _options);
                WorldReplaced?.Invoke(_world);
            }
            else if (!handshake.WillAdoptSeed)
            {
                Fault("seed mismatch");
                return;
            }
        }

        string? mismatch =
            handshake.MapDigest != MapDigest.Of(_world.Map) ? "map mismatch" :
            handshake.InputDelayTicks != _inputDelayTicks ? "input-delay mismatch" :
            (Sim.FactionId)handshake.FactionAllies != _options.FactionAllies ? "faction mismatch" :
            (Sim.FactionId)handshake.FactionCentral != _options.FactionCentral ? "faction mismatch" :
            null;

        if (mismatch is not null)
        {
            Fault(mismatch);
            return;
        }

        Status = SessionStatus.Running;

        // Replay anything that arrived during the handshake.
        foreach (var raw in _earlyPackets)
            HandlePacket(raw);
        _earlyPackets.Clear();
    }

    private void HandleInputFrame(ReadOnlySpan<byte> packet)
    {
        if (!Protocol.TryDecodeFrame(packet, out int tick, out var commands, out var error))
        {
            Fault($"bad input frame: {error}");
            return;
        }

        if (tick <= _lastExecutedTick || tick > _lastExecutedTick + 512)
            return; // stale or absurdly far future: ignore

        _peerFrames[tick] = commands;
    }

    private void HandleHashReport(ReadOnlySpan<byte> packet)
    {
        var (tick, remoteHash) = Protocol.DecodeHashReport(packet);
        _peerHashes[tick] = remoteHash;

        if (_myHashes.TryGetValue(tick, out ulong mine))
            CompareHashes(tick, mine, remoteHash);
    }

    private void CompareHashes(int tick, ulong mine, ulong theirs)
    {
        if (mine == theirs)
            return;

        Status = SessionStatus.Desynced;
        Desync = new DesyncReport(tick, mine, theirs);
    }

    // --- sending ---

    private void PublishDueFrames()
    {
        int horizon = _lastExecutedTick + 1 + _inputDelayTicks;
        while (_nextFrameToSend <= horizon)
        {
            var commands = new List<Command>();
            while (_localOutbox.Count > 0)
                commands.Add(_localOutbox.Dequeue());

            _myFrames[_nextFrameToSend] = commands;
            _transport.Send(Protocol.EncodeFrame(_nextFrameToSend, commands));
            _nextFrameToSend++;
        }
    }

    // --- execution ---

    private void ExecuteEligibleTicks()
    {
        while (_myFrames.ContainsKey(_lastExecutedTick + 1) &&
               _peerFrames.TryGetValue(_lastExecutedTick + 1, out var peerCommands))
        {
            int tick = _lastExecutedTick + 1;
            var mine = _myFrames[tick];

            _world.Step(CanonicalMerge(mine, peerCommands));
            _lastExecutedTick = tick;

            ulong hash = _world.StateHash();
            _myHashes[tick] = hash;
            _transport.Send(Protocol.EncodeHashReport(tick, hash));

            if (_peerHashes.Remove(tick, out ulong reported))
                CompareHashes(tick, hash, reported);

            if (Status != SessionStatus.Running)
                return;

            PruneOldState();
        }
    }

    private void PruneOldState()
    {
        const int retentionTicks = 512;
        if (_lastExecutedTick <= retentionTicks)
            return;

        int cutoff = _lastExecutedTick - retentionTicks;
        PruneDict(_myHashes, cutoff);
        PruneDict(_peerHashes, cutoff);

        int frameCutoff = _lastExecutedTick - 64;
        PruneDict(_myFrames, frameCutoff);
        PruneDict(_peerFrames, frameCutoff);
    }

    private static void PruneDict<K, V>(Dictionary<K, V> dict, K cutoff) where K : notnull
    {
        List<K>? doomed = null;
        foreach (var k in dict.Keys)
        {
            if (Comparer<K>.Default.Compare(k, cutoff) < 0)
                (doomed ??= new List<K>()).Add(k);
        }
        if (doomed is not null)
            foreach (var k in doomed)
                dict.Remove(k);
    }

    /// <summary>
    /// Merge both players' commands into one deterministically ordered list so both
    /// peers apply them in identical sequence regardless of who sent first.
    /// </summary>
    public static IReadOnlyList<Command> CanonicalMerge(List<Command> mine, List<Command> theirs)
    {
        var merged = new List<Command>(mine.Count + theirs.Count);
        merged.AddRange(mine);
        merged.AddRange(theirs);
        merged.Sort((a, b) =>
        {
            int c = a.UnitId.CompareTo(b.UnitId);
            if (c != 0) return c;
            c = ((byte)a.Type).CompareTo(((byte)b.Type));
            if (c != 0) return c;
            c = a.Pos.X.Raw.CompareTo(b.Pos.X.Raw);
            if (c != 0) return c;
            c = a.Pos.Y.Raw.CompareTo(b.Pos.Y.Raw);
            if (c != 0) return c;
            c = a.Alt.X.Raw.CompareTo(b.Alt.X.Raw);
            if (c != 0) return c;
            c = a.Alt.Y.Raw.CompareTo(b.Alt.Y.Raw);
            if (c != 0) return c;
            return a.Param.CompareTo(b.Param);
        });
        return merged;
    }

    private void Fault(string reason)
    {
        Status = SessionStatus.Faulted;
        FaultReason = reason;
    }
}
