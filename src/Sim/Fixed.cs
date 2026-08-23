namespace Sim;

/// <summary>
/// Signed Q32.32 fixed-point number. The only scalar type used inside the simulation.
/// All operations are pure integer arithmetic, so results are bit-identical on every
/// platform and every run. Range is roughly +/- 2^31 with a resolution of 2^-32.
/// </summary>
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
    public const int Shift = 32;
    public const long OneRaw = 1L << Shift;

    public static readonly Fixed Zero = new(0L);
    public static readonly Fixed One = new(OneRaw);
    public static readonly Fixed Half = new(OneRaw / 2);
    public static readonly Fixed MaxValue = new(long.MaxValue);
    public static readonly Fixed MinValue = new(long.MinValue);

    /// <summary>Internal representation: value * 2^32.</summary>
    public readonly long Raw;

    private Fixed(long raw) => Raw = raw;

    public static Fixed FromRaw(long raw) => new(raw);
    public static Fixed FromInt(int v) => new((long)v << Shift);

    /// <summary>num/den computed exactly in integer arithmetic at full precision, rounded to nearest.</summary>
    public static Fixed FromRatio(long num, long den)
    {
        if (den == 0) throw new DivideByZeroException("Fixed.FromRatio");
        return new(DivideRaw(num, den));
    }

    /// <summary>(num &lt;&lt; 32) / den with round-to-nearest. Pure integer, deterministic.</summary>
    private static long DivideRaw(long num, long den)
    {
        Int128 n = (Int128)num << Shift;
        // Round half away from zero so positive and negative results are symmetric.
        if ((n >= 0) == (den >= 0))
            n += den / 2;
        else
            n -= den / 2;
        return (long)(n / den);
    }

    public int ToInt() => (int)(Raw >> Shift);

    public static explicit operator double(Fixed f) => f.Raw / (double)OneRaw;

    public static Fixed operator +(Fixed a, Fixed b) => new(a.Raw + b.Raw);
    public static Fixed operator -(Fixed a, Fixed b) => new(a.Raw - b.Raw);
    public static Fixed operator -(Fixed a) => new(-a.Raw);
    public static Fixed operator *(Fixed a, Fixed b) => new((long)((Int128)a.Raw * b.Raw >> Shift));

    public static Fixed operator /(Fixed a, Fixed b)
    {
        if (b.Raw == 0) throw new DivideByZeroException("Fixed division by zero");
        return new(DivideRaw(a.Raw, b.Raw));
    }

    public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
    public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;
    public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
    public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
    public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
    public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;

    public bool Equals(Fixed other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fixed f && Raw == f.Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

    public static Fixed Abs(Fixed a) => a.Raw < 0 ? new(-a.Raw) : a;
    public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;
    public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;

    public static Fixed Clamp(Fixed v, Fixed lo, Fixed hi)
    {
        if (v.Raw < lo.Raw) return lo;
        if (v.Raw > hi.Raw) return hi;
        return v;
    }

    /// <summary>
    /// Integer-only Newton-Raphson square root. Solves r*r ~= x << 32 in the raw domain,
    /// so the result is fully deterministic. Rounds to within one unit-in-the-last-place.
    /// </summary>
    public static Fixed Sqrt(Fixed x)
    {
        if (x.Raw <= 0) return Zero;
        long target = x.Raw;

        // Initial guess: 2^(bitLength/2 + 16), which brackets sqrt(target << 32).
        int bits = 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)target);
        long guess = 1L << (bits / 2 + 16);

        for (int i = 0; i < 64; i++)
        {
            long next = (long)((((Int128)target << Shift) / guess + guess) >> 1);
            if (next == guess || next == guess - 1 || next == guess + 1)
            {
                // Settle oscillation: pick whichever of guess/next is closer.
                Int128 sq = (Int128)target << Shift;
                Int128 errGuess = (Int128)guess * guess - sq;
                if (errGuess < 0) errGuess = -errGuess;
                Int128 errNext = (Int128)next * next - sq;
                if (errNext < 0) errNext = -errNext;
                return FromRaw(errNext < errGuess ? next : guess);
            }
            guess = next;
        }
        return FromRaw(guess);
    }

    public override string ToString() => ((double)this).ToString("0.###");
}
