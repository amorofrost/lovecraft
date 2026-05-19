using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services;

public interface IBroadcastService
{
    Task<BroadcastDto> CreateAsync(CreateBroadcastRequestDto request, string issuedByUserId);
    Task<BroadcastDto?> GetByIdAsync(string broadcastId);
    Task<List<BroadcastDto>> ListAsync(int limit = 50);
    Task SetEstimatedRecipientsAsync(string broadcastId, int count);
    Task SetCompletedAsync(string broadcastId, int dispatchedCount, DateTime completedAtUtc);
}
