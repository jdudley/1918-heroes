namespace Sim;

/// <summary>
/// FNV-1a 64-bit state hasher. Walks world state in a fixed field order,
/// mixing raw integer representations only. Bump <see cref="Salt"/> whenever
/// the state schema changes to invalidate stale baseline hashes.
/// </summary>
public struct Hasher
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    public const ulong Salt = 0x1918C0DECAFEF00DUL;

    private ulong _h;

    public Hasher() => _h = FnvOffset ^ Salt;

    public void Mix(long v)
    {
        Mix((ulong)v);
    }

    public void Mix(ulong v)
    {
        _h ^= v & 0xFF; _h *= FnvPrime;
        _h ^= (v >> 8) & 0xFF; _h *= FnvPrime;
        _h ^= (v >> 16) & 0xFF; _h *= FnvPrime;
        _h ^= (v >> 24) & 0xFF; _h *= FnvPrime;
        _h ^= (v >> 32) & 0xFF; _h *= FnvPrime;
        _h ^= (v >> 40) & 0xFF; _h *= FnvPrime;
        _h ^= (v >> 48) & 0xFF; _h *= FnvPrime;
        _h ^= (v >> 56) & 0xFF; _h *= FnvPrime;
    }

    public void Mix(int v) => Mix((long)v);

    public void Mix(bool v) => Mix(v ? 1L : 0L);

    public void Mix(Fixed v) => Mix(v.Raw);

    public void Mix(Fixed2 v)
    {
        Mix(v.X);
        Mix(v.Y);
    }

    public readonly ulong Digest => _h;
}
