using Xunit;

namespace Sim.Tests;

public class RngTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = Rng.FromSeed(42);
        var b = Rng.FromSeed(42);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextU32(), b.NextU32());
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        var a = Rng.FromSeed(1);
        var b = Rng.FromSeed(2);
        int differences = 0;
        for (int i = 0; i < 100; i++)
            if (a.NextU32() != b.NextU32()) differences++;
        Assert.True(differences > 90);
    }

    [Fact]
    public void Range_StaysInBounds()
    {
        var rng = Rng.FromSeed(7);
        for (int i = 0; i < 10_000; i++)
        {
            int v = rng.Range(3, 9);
            Assert.InRange(v, 3, 8);
        }
    }

    [Fact]
    public void Range_CoversFullSpread()
    {
        var rng = Rng.FromSeed(9);
        bool sawLow = false, sawHigh = false;
        for (int i = 0; i < 10_000 && !(sawLow && sawHigh); i++)
        {
            int v = rng.Range(0, 2);
            sawLow |= v == 0;
            sawHigh |= v == 1;
        }
        Assert.True(sawLow && sawHigh);
    }

    [Fact]
    public void Chance_EdgesAreExact()
    {
        var rng = Rng.FromSeed(1);
        Assert.False(rng.Chance(Fixed.Zero));
        Assert.True(rng.Chance(Fixed.One));
    }

    [Fact]
    public void Chance_HalfIsRoughlyHalf()
    {
        var rng = Rng.FromSeed(11);
        int hits = 0;
        const int trials = 20_000;
        for (int i = 0; i < trials; i++)
            if (rng.Chance(Fixed.Half)) hits++;
        // Tolerance is generous; this guards against gross bias only.
        Assert.InRange(hits, trials / 2 - trials / 10, trials / 2 + trials / 10);
    }
}
