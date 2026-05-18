using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services;

public class MockPushSubscriptionService : IPushSubscriptionService
{
    public Task<WebPushSubscriptionDto> SubscribeAsync(string userId, WebPushSubscriptionRequestDto request)
    {
        var deviceId = string.IsNullOrEmpty(request.DeviceId) ? Guid.NewGuid().ToString("N") : request.DeviceId;
        var now = DateTime.UtcNow;
        var dto = new WebPushSubscriptionDto
        {
            DeviceId = deviceId,
            Endpoint = request.Endpoint,
            P256dh = request.P256dh,
            Auth = request.Auth,
            UserAgent = request.UserAgent,
            CreatedAtUtc = MockDataStore.PushSubscriptions.TryGetValue((userId, deviceId), out var existing)
                ? existing.CreatedAtUtc : now,
            LastSeenAtUtc = now,
        };
        MockDataStore.PushSubscriptions[(userId, deviceId)] = dto;
        return Task.FromResult(dto);
    }

    public Task<List<WebPushSubscriptionDto>> ListAsync(string userId) =>
        Task.FromResult(MockDataStore.PushSubscriptions
            .Where(kv => kv.Key.UserId == userId)
            .Select(kv => kv.Value)
            .ToList());

    public Task<int> CountAsync(string userId) =>
        Task.FromResult(MockDataStore.PushSubscriptions.Count(kv => kv.Key.UserId == userId));

    public Task<bool> UnsubscribeAsync(string userId, string deviceId) =>
        Task.FromResult(MockDataStore.PushSubscriptions.TryRemove((userId, deviceId), out _));
}
