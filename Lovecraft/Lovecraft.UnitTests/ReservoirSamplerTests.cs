using System.Linq;
using Lovecraft.Backend.Helpers;
using Xunit;

namespace Lovecraft.UnitTests;

public class ReservoirSamplerTests
{
    [Fact]
    public void Sample_ZeroOrNegativeCount_ReturnsEmpty()
    {
        Assert.Empty(ReservoirSampler.Sample(Enumerable.Range(1, 10), 0));
        Assert.Empty(ReservoirSampler.Sample(Enumerable.Range(1, 10), -5));
    }

    [Fact]
    public void Sample_EmptySource_ReturnsEmpty()
    {
        Assert.Empty(ReservoirSampler.Sample(Enumerable.Empty<int>(), 5));
    }

    [Fact]
    public void Sample_CountGreaterThanSource_ReturnsWholeSet()
    {
        var src = Enumerable.Range(1, 3).ToList();
        var result = ReservoirSampler.Sample(src, 10);

        Assert.Equal(3, result.Count);
        Assert.Equal(src.OrderBy(x => x), result.OrderBy(x => x));
    }

    [Fact]
    public void Sample_ReturnsRequestedCount_AsDistinctSubset()
    {
        var src = Enumerable.Range(1, 100).ToList();
        var result = ReservoirSampler.Sample(src, 10);

        Assert.Equal(10, result.Count);
        Assert.Equal(10, result.Distinct().Count());
        Assert.All(result, x => Assert.Contains(x, src));
    }

    [Fact]
    public void Sample_EnumeratesSourceExactlyOnce()
    {
        var enumerations = 0;
        IEnumerable<int> Counting()
        {
            enumerations++;
            for (var i = 0; i < 50; i++) yield return i;
        }

        _ = ReservoirSampler.Sample(Counting(), 5);

        Assert.Equal(1, enumerations);
    }

    [Fact]
    public void Sample_EveryItemIsReachable()
    {
        // Loose uniformity check: over many draws of 2-from-5, every item should appear.
        var src = Enumerable.Range(0, 5).ToList();
        var seen = new HashSet<int>();
        for (var run = 0; run < 500; run++)
            foreach (var x in ReservoirSampler.Sample(src, 2))
                seen.Add(x);

        Assert.Equal(5, seen.Count);
    }
}
