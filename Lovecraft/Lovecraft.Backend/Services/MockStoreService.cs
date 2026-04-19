using Lovecraft.Common.DTOs.Store;
using Lovecraft.Backend.MockData;

namespace Lovecraft.Backend.Services;

public class MockStoreService : IStoreService
{
    public Task<List<StoreItemDto>> GetStoreItemsAsync()
    {
        return Task.FromResult(MockDataStore.StoreItems);
    }

    public Task<StoreItemDto?> GetStoreItemByIdAsync(string itemId)
    {
        var item = MockDataStore.StoreItems.FirstOrDefault(i => i.Id == itemId);
        return Task.FromResult(item);
    }

    public Task<StoreItemDto> CreateStoreItemAsync(StoreItemDto item)
    {
        if (MockDataStore.StoreItems.Any(i => i.Id == item.Id))
            throw new InvalidOperationException($"Store item '{item.Id}' already exists.");
        MockDataStore.StoreItems.Add(item);
        return Task.FromResult(item);
    }

    public Task<StoreItemDto?> UpdateStoreItemAsync(string itemId, StoreItemDto item)
    {
        var existing = MockDataStore.StoreItems.FirstOrDefault(i => i.Id == itemId);
        if (existing is null)
            return Task.FromResult<StoreItemDto?>(null);
        existing.Title = item.Title;
        existing.Description = item.Description;
        existing.Price = item.Price;
        existing.ImageUrl = item.ImageUrl;
        existing.Category = item.Category;
        existing.ExternalPurchaseUrl = item.ExternalPurchaseUrl;
        return Task.FromResult<StoreItemDto?>(existing);
    }

    public Task<bool> DeleteStoreItemAsync(string itemId)
    {
        var ix = MockDataStore.StoreItems.FindIndex(i => i.Id == itemId);
        if (ix < 0)
            return Task.FromResult(false);
        MockDataStore.StoreItems.RemoveAt(ix);
        return Task.FromResult(true);
    }
}
