namespace Lovecraft.Backend.Services.Metrics;

/// <summary>
/// Normalizes a request path or route template into a low-cardinality,
/// Azure-Table-safe dimension segment. Resource IDs collapse to {id};
/// route-template constraints are stripped; segments are joined with '~'
/// (Azure PK/RK forbids '/', '\', '#', '?').
/// </summary>
public static class MetricsRouteNormalizer
{
    private static readonly char[] ConstraintChars = { ':', '=' };

    public static string Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;

        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
            segments[i] = NormalizeSegment(segments[i]);

        return string.Join('~', segments);
    }

    private static string NormalizeSegment(string seg)
    {
        // Route-template token: {id}, {id:int}, {id=5}, {*catchAll}
        if (seg.Length >= 2 && seg[0] == '{' && seg[^1] == '}')
        {
            var inner = seg[1..^1].TrimStart('*');         // catch-all {*x}/{**x}
            var cut = inner.IndexOfAny(ConstraintChars);
            if (cut >= 0) inner = inner[..cut];            // strip constraint/default
            // Defensive: a malformed/empty token must never leak a bare "{}" into the key.
            return inner.Length == 0 ? "{id}" : "{" + inner + "}";
        }

        // Heuristic for non-templated paths (404s, unmatched routes).
        // Negative ints (e.g. campaign event id -1) intentionally collapse too.
        if (Guid.TryParse(seg, out _)) return "{id}";
        if (long.TryParse(seg, out _)) return "{id}";
        return seg;
    }
}
