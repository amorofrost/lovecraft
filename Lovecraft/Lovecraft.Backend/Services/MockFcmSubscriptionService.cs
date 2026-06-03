using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services;

public class MockFcmSubscriptionService : IFcmSubscriptionService
{
    public Task<FcmSubscriptionDto> RegisterAsync(string userId, FcmRegisterRequestDto request)
    {
        var deviceId = string.IsNullOrEmpty(request.DeviceId) ? Guid.NewGuid().ToString("N") : request.DeviceId;
        var now = DateTime.UtcNow;
        var dto = new FcmSubscriptionDto
        {
            DeviceId = deviceId,
            Token = request.Token,
            Platform = string.IsNullOrEmpty(request.Platform) ? "android" : request.Platform,
            DeviceModel = request.DeviceModel,
            CreatedAtUtc = MockDataStore.FcmSubscriptions.TryGetValue((userId, deviceId), out var existing)
                ? existing.CreatedAtUtc : now,
            LastSeenAtUtc = now,
        };
        MockDataStore.FcmSubscriptions[(userId, deviceId)] = dto;
        return Task.FromResult(dto);
    }

    public Task<List<FcmSubscriptionDto>> ListAsync(string userId) =>
        Task.FromResult(MockDataStore.FcmSubscriptions
            .Where(kv => kv.Key.UserId == userId)
            .Select(kv => kv.Value)
            .ToList());

    public Task<int> CountAsync(string userId) =>
        Task.FromResult(MockDataStore.FcmSubscriptions.Count(kv => kv.Key.UserId == userId));

    public Task<bool> UnregisterAsync(string userId, string deviceId) =>
        Task.FromResult(MockDataStore.FcmSubscriptions.TryRemove((userId, deviceId), out _));
}
