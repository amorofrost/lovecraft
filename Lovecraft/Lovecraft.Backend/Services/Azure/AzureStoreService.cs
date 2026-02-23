using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Store;

namespace Lovecraft.Backend.Services.Azure;

public class AzureStoreService : IStoreService
{
    private readonly TableClient _storeTable;
    private readonly ILogger<AzureStoreService> _logger;

    public AzureStoreService(TableServiceClient tableServiceClient, ILogger<AzureStoreService> logger)
    {
        _logger = logger;
        _storeTable = tableServiceClient.GetTableClient(TableNames.StoreItems);
        _storeTable.CreateIfNotExistsAsync().GetAwaiter().GetResult();
    }

    public async Task<List<StoreItemDto>> GetStoreItemsAsync()
    {
        var results = new List<StoreItemDto>();
        await foreach (var entity in _storeTable.QueryAsync<StoreItemEntity>(filter: "PartitionKey eq 'STORE'"))
        {
            results.Add(ToDto(entity));
        }
        return results;
    }

    public async Task<StoreItemDto?> GetStoreItemByIdAsync(string itemId)
    {
        try
        {
            var response = await _storeTable.GetEntityAsync<StoreItemEntity>("STORE", itemId);
            return ToDto(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static StoreItemDto ToDto(StoreItemEntity entity) => new StoreItemDto
    {
        Id = entity.RowKey,
        Title = entity.Title,
        Description = entity.Description,
        Price = Convert.ToDecimal(entity.Price),
        ImageUrl = entity.ImageUrl,
        Category = entity.Category,
        ExternalPurchaseUrl = entity.ExternalPurchaseUrl
    };
}
