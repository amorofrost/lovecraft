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
}
