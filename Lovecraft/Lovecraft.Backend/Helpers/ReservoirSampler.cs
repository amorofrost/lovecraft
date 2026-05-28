namespace Lovecraft.Backend.Helpers;

/// <summary>
/// Uniform random sampling of up to <c>count</c> items from a sequence of unknown
/// length in a single pass (Algorithm R). Uses O(count) memory — it never
/// materializes or shuffles the whole source, so callers can sample a small deck
/// out of a very large population without copying it.
/// </summary>
public static class ReservoirSampler
{
    public static List<T> Sample<T>(IEnumerable<T> source, int count)
    {
        if (count <= 0) return new List<T>();

        var reservoir = new List<T>(count);
        var seen = 0;
        foreach (var item in source)
        {
            if (reservoir.Count < count)
            {
                reservoir.Add(item);
            }
            else
            {
                // Each later item replaces a random reservoir slot with probability count/seen,
                // which keeps every seen item equally likely to be retained.
                var j = Random.Shared.Next(seen + 1);
                if (j < count) reservoir[j] = item;
            }
            seen++;
        }

        // Algorithm R yields a uniform subset but a biased internal order
        // (the initial fill keeps its positions). Shuffle the reservoir — O(count) —
        // so the returned order is itself random (matters for a swipe deck).
        for (var i = reservoir.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (reservoir[i], reservoir[j]) = (reservoir[j], reservoir[i]);
        }

        return reservoir;
    }
}
