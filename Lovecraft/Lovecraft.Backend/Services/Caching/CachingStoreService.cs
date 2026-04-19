using Microsoft.Extensions.Caching.Memory;
using Lovecraft.Common.DTOs.Store;

namespace Lovecraft.Backend.Services.Caching;

/// <summary>
/// Caching decorator for IStoreService. Reads use a 5-minute TTL; writes invalidate
/// the all-items cache and the affected item key.
/// </summary>
public class CachingStoreService : IStoreService
{
    private readonly IStoreService _inner;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const string AllKey = "store:all";
    private static string ItemKey(string id) => $"store:{id}";

    public CachingStoreService(IStoreService inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public async Task<List<StoreItemDto>> GetStoreItemsAsync()
    {
        if (_cache.TryGetValue(AllKey, out List<StoreItemDto>? cached) && cached is not null)
            return cached;

        var result = await _inner.GetStoreItemsAsync();
        _cache.Set(AllKey, result, Ttl);
        return result;
    }

    public async Task<StoreItemDto?> GetStoreItemByIdAsync(string itemId)
    {
        var key = ItemKey(itemId);
        if (_cache.TryGetValue(key, out StoreItemDto? cached))
            return cached;

        var result = await _inner.GetStoreItemByIdAsync(itemId);
        if (result is not null)
            _cache.Set(key, result, Ttl);
        return result;
    }

    public async Task<StoreItemDto> CreateStoreItemAsync(StoreItemDto item)
    {
        var r = await _inner.CreateStoreItemAsync(item);
        _cache.Remove(AllKey);
        _cache.Remove(ItemKey(r.Id));
        return r;
    }

    public async Task<StoreItemDto?> UpdateStoreItemAsync(string itemId, StoreItemDto item)
    {
        var r = await _inner.UpdateStoreItemAsync(itemId, item);
        _cache.Remove(AllKey);
        _cache.Remove(ItemKey(itemId));
        if (r is not null)
            _cache.Remove(ItemKey(r.Id));
        return r;
    }

    public async Task<bool> DeleteStoreItemAsync(string itemId)
    {
        var ok = await _inner.DeleteStoreItemAsync(itemId);
        if (ok)
        {
            _cache.Remove(AllKey);
            _cache.Remove(ItemKey(itemId));
        }
        return ok;
    }
}
