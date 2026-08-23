namespace Sim;

/// <summary>
/// PCG-XSH-RR (pcg32): small, fast, high-quality deterministic PRNG.
/// The state lives inside the World so replays, saves, and state hashes are exact.
/// Never use System.Random or any other entropy source inside the simulation.
/// </summary>
public struct Rng
{
    private ulong _state;
    private ulong _inc;

    /// <summary>Raw state, exposed for hashing and serialization only.</summary>
    public readonly ulong StateA => _state;
    public readonly ulong StateB => _inc;

    public static Rng FromSeed(ulong seed)
    {
        var rng = default(Rng);
        rng._state = 0;
        rng._inc = (seed << 1) | 1UL;
        _ = rng.NextU32();
        rng._state += 1442695040888963407UL;
        _ = rng.NextU32();
        return rng;
    }

    public uint NextU32()
    {
        ulong old = _state;
        _state = old * 6364136223846793005UL + _inc;
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    /// <summary>Uniform in [minInclusive, maxExclusive).</summary>
    public int Range(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentException($"Range({minInclusive}, {maxExclusive}): max must exceed min");
        uint span = (uint)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextU32() % span);
    }

    /// <summary>True with probability p, where p is a Fixed in [0, 1].</summary>
    public bool Chance(Fixed p)
    {
        if (p.Raw <= 0) return false;
        if (p.Raw >= Fixed.OneRaw) return true;
        // p.Raw for p in [0,1) is exactly a 32-bit fraction of the full range.
        return NextU32() < (uint)p.Raw;
    }
}
