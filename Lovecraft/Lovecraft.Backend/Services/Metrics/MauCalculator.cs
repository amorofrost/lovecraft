using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace Lovecraft.Backend.Services.Metrics;

public sealed class MauCalculator
{
    private readonly TableServiceClient? _tables;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public MauCalculator(TableServiceClient? tables, IMemoryCache cache)
    {
        _tables = tables;
        _cache = cache;
    }

    public async Task<int> GetMauAsync(DateOnly today, CancellationToken ct = default)
    {
        if (_tables is null) return 0;
        var key = $"mau:{today:yyyy-MM-dd}";
        return await _cache.GetOrCreateAsync(key, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheTtl;
            var table = _tables.GetTableClient(TableNames.DailyActiveUsers);
            await table.CreateIfNotExistsAsync(ct);
            var partitions = new Dictionary<string, string[]>();
            for (int d = 0; d < 30; d++)
            {
                var pk = today.AddDays(-d).ToString("yyyy-MM-dd");
                var ids = new List<string>();
                await foreach (var row in table.QueryAsync<TableEntity>(
                    filter: $"PartitionKey eq '{pk}'", select: new[] { "RowKey" }, cancellationToken: ct))
                    ids.Add(row.RowKey);
                partitions[pk] = ids.ToArray();
            }
            return ComputeFromPartitions(partitions);
        });
    }

    public static int ComputeFromPartitions(IReadOnlyDictionary<string, string[]> partitions)
    {
        var seen = new HashSet<string>();
        foreach (var ids in partitions.Values)
            foreach (var id in ids) seen.Add(id);
        return seen.Count;
    }

    public async Task<int> GetDauAsync(DateOnly day, CancellationToken ct = default)
    {
        if (_tables is null) return 0;
        var table = _tables.GetTableClient(TableNames.DailyActiveUsers);
        await table.CreateIfNotExistsAsync(ct);
        var pk = day.ToString("yyyy-MM-dd");
        int count = 0;
        await foreach (var _ in table.QueryAsync<TableEntity>(filter: $"PartitionKey eq '{pk}'", select: new[] { "RowKey" }, cancellationToken: ct))
            count++;
        return count;
    }
}
