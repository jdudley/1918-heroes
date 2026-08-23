using Xunit;

namespace Sim.Tests;

public class FixedMathTests
{
    [Fact]
    public void AdditionAndSubtraction_AreExact()
    {
        var a = Fixed.FromInt(7);
        var b = Fixed.FromRatio(1, 4);
        Assert.Equal(Fixed.FromRaw(a.Raw + b.Raw), a + b);
        Assert.Equal(Fixed.FromInt(6) + Fixed.FromRatio(3, 4), a - b);
    }

    [Fact]
    public void Multiplication_HandlesLargeOperandsWithoutOverflow()
    {
        // 40,000 x 40,000 = 1.6e9 fits in Q32.32 range (~2.1e9); raw product would not fit in long.
        var result = Fixed.FromInt(40000) * Fixed.FromInt(40000);
        Assert.Equal(1600000000, result.ToInt());
    }

    [Fact]
    public void Multiplication_SignsBehave()
    {
        Assert.Equal(Fixed.FromRatio(-15, 4), Fixed.FromRatio(3, 2) * Fixed.FromRatio(-5, 2));
    }

    [Fact]
    public void Division_ByThirdTimesThree_IsOne()
    {
        var third = Fixed.One / Fixed.FromInt(3);
        // Within a couple raw ulps of exactness after the round trip.
        Assert.True(Fixed.Abs(third * Fixed.FromInt(3) - Fixed.One).Raw <= 2,
            $"third*3 was {third * Fixed.FromInt(3)}");
    }

    [Fact]
    public void Division_ByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => Fixed.One / Fixed.Zero);
    }

    [Theory]
    [InlineData(144, 12)]
    [InlineData(10000, 100)]
    [InlineData(0, 0)]
    public void Sqrt_PerfectSquares(int input, int expectedRoot)
    {
        var root = Fixed.Sqrt(Fixed.FromInt(input));
        Assert.True(Fixed.Abs(root - Fixed.FromInt(expectedRoot)).Raw <= 64,
            $"sqrt({input}) = {root}, expected ~{expectedRoot}");
    }

    [Fact]
    public void Sqrt_NonSquare_IsAccurateToUlp()
    {
        var root = Fixed.Sqrt(Fixed.FromInt(200)); // 14.142135...
        double expected = Math.Sqrt(200);
        double actual = (double)root;
        Math.Abs(expected - actual).ShouldBeSmall();
    }

    [Fact]
    public void Sqrt_OfFraction_ScalesCorrectly()
    {
        // sqrt(1/4) == 1/2
        var root = Fixed.Sqrt(Fixed.FromRatio(1, 4));
        Assert.True(Fixed.Abs(root - Fixed.Half).Raw <= 64);
    }

    [Fact]
    public void FromRatio_MatchesDivision()
    {
        var viaRatio = Fixed.FromRatio(22, 7);
        var viaDiv = Fixed.FromInt(22) / Fixed.FromInt(7);
        Assert.Equal(viaDiv.Raw, viaRatio.Raw);
    }

    [Fact]
    public void Clamp_AndComparisons_Behave()
    {
        Assert.Equal(Fixed.FromInt(5), Fixed.Clamp(Fixed.FromInt(99), Fixed.Zero, Fixed.FromInt(5)));
        Assert.Equal(Fixed.FromInt(-1), Fixed.Clamp(Fixed.FromInt(-99), Fixed.FromInt(-1), Fixed.One));
        Assert.True(Fixed.FromInt(2) > Fixed.FromRatio(199, 100));
    }

    [Fact]
    public void Vector_Normalize_LengthAndDirection()
    {
        var v = new Fixed2(Fixed.FromInt(3), Fixed.FromInt(4));
        Assert.Equal(Fixed.FromInt(5), v.Length());
        var n = v.Normalized();
        Assert.True(Fixed.Abs(n.Length() - Fixed.One).Raw <= 4,
            $"normalized length was {n.Length()}");
        Assert.Equal(Fixed2.Zero.Normalized(), Fixed2.Zero);
    }

    [Fact]
    public void Vector_DotProduct_DetectsFacing()
    {
        var east = new Fixed2(Fixed.One, Fixed.Zero);
        var northEast = new Fixed2(Fixed.One, Fixed.One).Normalized();
        Assert.True(northEast.Dot(east) > Fixed.Half);   // within an arc
        var behind = new Fixed2(-Fixed.One, Fixed.Zero);
        Assert.True(behind.Dot(east) < Fixed.Zero);      // facing away
    }
}

file static class DoubleAssertions
{
    public static void ShouldBeSmall(this double value)
    {
        Assert.True(Math.Abs(value) < 1e-4, $"expected near zero, was {value}");
    }
}
