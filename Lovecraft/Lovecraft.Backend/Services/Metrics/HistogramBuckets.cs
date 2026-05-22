namespace Lovecraft.Backend.Services.Metrics;

public static class HistogramBuckets
{
    public static readonly double[] Boundaries = { 25, 50, 100, 250, 500, 1000, 2500, 5000 };
    public const int BucketCount = 9;

    public static int IndexFor(double ms)
    {
        for (int i = 0; i < Boundaries.Length; i++)
            if (ms <= Boundaries[i]) return i;
        return BucketCount - 1;
    }

    public static long[] Empty() => new long[BucketCount];
}
