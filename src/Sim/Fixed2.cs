namespace Sim;

/// <summary>
/// 2D vector of Fixed components. All operations are integer-exact.
/// Angles are avoided entirely: facing is stored as a unit direction vector,
/// and arc checks later use dot products.
/// </summary>
public readonly struct Fixed2 : IEquatable<Fixed2>
{
    public static readonly Fixed2 Zero = new(Fixed.Zero, Fixed.Zero);
    public static readonly Fixed2 One = new(Fixed.One, Fixed.One);

    public readonly Fixed X;
    public readonly Fixed Y;

    public Fixed2(Fixed x, Fixed y)
    {
        X = x;
        Y = y;
    }

    public static Fixed2 operator +(Fixed2 a, Fixed2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Fixed2 operator -(Fixed2 a, Fixed2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Fixed2 operator -(Fixed2 a) => new(-a.X, -a.Y);
    public static Fixed2 operator *(Fixed2 a, Fixed s) => new(a.X * s, a.Y * s);
    public static Fixed2 operator *(Fixed s, Fixed2 a) => a * s;
    public static Fixed2 operator /(Fixed2 a, Fixed s) => new(a.X / s, a.Y / s);
    public static bool operator ==(Fixed2 a, Fixed2 b) => a.X == b.X && a.Y == b.Y;
    public static bool operator !=(Fixed2 a, Fixed2 b) => !(a == b);

    public Fixed Dot(Fixed2 o) => X * o.X + Y * o.Y;

    /// <summary>Squared euclidean length. Use this for all distance comparisons.</summary>
    public Fixed LengthSquared() => X * X + Y * Y;

    public Fixed Length() => Fixed.Sqrt(LengthSquared());

    /// <summary>
    /// Unit vector in the same direction; Zero if this vector is Zero.
    /// </summary>
    public Fixed2 Normalized()
    {
        Fixed len = Length();
        if (len.Raw == 0) return Zero;
        return new Fixed2(X / len, Y / len);
    }

    public Fixed DistanceSquaredTo(Fixed2 o) => (this - o).LengthSquared();
    public Fixed DistanceTo(Fixed2 o) => Fixed.Sqrt(DistanceSquaredTo(o));

    public bool Equals(Fixed2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Fixed2 f && Equals(f);
    public override int GetHashCode() => HashCode.Combine(X.Raw, Y.Raw);
    public override string ToString() => $"({X}, {Y})";
}
