using System.Collections.Concurrent;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage.Entities;

namespace Lovecraft.Backend.Services.Caching;

public class UserCache
{
    private readonly ConcurrentDictionary<string, UserEntity> _cache = new();

    public async Task LoadAsync(TableClient usersTable)
    {
        await foreach (var entity in usersTable.QueryAsync<UserEntity>())
            _cache[entity.RowKey] = entity;
    }

    public UserEntity? Get(string userId) =>
        _cache.TryGetValue(userId, out var e) ? e : null;

    public List<UserEntity> GetAll() => _cache.Values.ToList();

    public void Set(UserEntity entity) => _cache[entity.RowKey] = entity;

    public void Remove(string userId) => _cache.TryRemove(userId, out _);
}
