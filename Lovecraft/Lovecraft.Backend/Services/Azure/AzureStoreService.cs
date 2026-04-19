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

    private static StoreItemEntity FromDto(StoreItemDto dto, string rowKey) => new StoreItemEntity
    {
        PartitionKey = "STORE",
        RowKey = rowKey,
        Title = dto.Title,
        Description = dto.Description,
        Price = (double)dto.Price,
        ImageUrl = dto.ImageUrl,
        Category = dto.Category,
        ExternalPurchaseUrl = dto.ExternalPurchaseUrl ?? string.Empty,
    };

    public async Task<StoreItemDto> CreateStoreItemAsync(StoreItemDto item)
    {
        try
        {
            await _storeTable.GetEntityAsync<StoreItemEntity>("STORE", item.Id);
            throw new InvalidOperationException($"Store item '{item.Id}' already exists.");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        var entity = FromDto(item, item.Id);
        await _storeTable.AddEntityAsync(entity);
        return ToDto(entity);
    }

    public async Task<StoreItemDto?> UpdateStoreItemAsync(string itemId, StoreItemDto item)
    {
        try
        {
            var response = await _storeTable.GetEntityAsync<StoreItemEntity>("STORE", itemId);
            var entity = response.Value;
            entity.Title = item.Title;
            entity.Description = item.Description;
            entity.Price = (double)item.Price;
            entity.ImageUrl = item.ImageUrl;
            entity.Category = item.Category;
            entity.ExternalPurchaseUrl = item.ExternalPurchaseUrl ?? string.Empty;
            await _storeTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
            return ToDto(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteStoreItemAsync(string itemId)
    {
        try
        {
            await _storeTable.DeleteEntityAsync("STORE", itemId, ETag.All);
            return true;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Delete store item {ItemId}", itemId);
            return false;
        }
    }
}
