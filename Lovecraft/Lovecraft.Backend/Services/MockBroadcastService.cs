using Lovecraft.Common.DTOs.Notifications;

namespace Lovecraft.Backend.Services;

public class MockBroadcastService : IBroadcastService
{
    private readonly List<BroadcastDto> _broadcasts = new();
    private readonly object _gate = new();

    public Task<BroadcastDto> CreateAsync(CreateBroadcastRequestDto request, string issuedByUserId)
    {
        var bc = new BroadcastDto
        {
            Id = $"bc-{Guid.NewGuid():N}".Substring(0, 16),
            Title = request.Title,
            Body = request.Body,
            Link = request.Link,
            Audience = request.Audience,
            IssuedByUserId = issuedByUserId,
            IssuedAtUtc = DateTime.UtcNow,
            EstimatedRecipients = 0,
            DispatchedCount = 0,
            Status = "pending",
            CompletedAtUtc = null,
        };
        lock (_gate) _broadcasts.Add(bc);
        return Task.FromResult(bc);
    }

    public Task<BroadcastDto?> GetByIdAsync(string broadcastId)
    {
        lock (_gate)
            return Task.FromResult(_broadcasts.FirstOrDefault(b => b.Id == broadcastId));
    }

    public Task<List<BroadcastDto>> ListAsync(int limit = 50)
    {
        lock (_gate)
            return Task.FromResult(_broadcasts.OrderByDescending(b => b.IssuedAtUtc).Take(limit).ToList());
    }

    public Task SetEstimatedRecipientsAsync(string broadcastId, int count)
    {
        lock (_gate)
        {
            var bc = _broadcasts.FirstOrDefault(b => b.Id == broadcastId);
            if (bc is not null) bc.EstimatedRecipients = count;
        }
        return Task.CompletedTask;
    }

    public Task SetCompletedAsync(string broadcastId, int dispatchedCount, DateTime completedAtUtc)
    {
        lock (_gate)
        {
            var bc = _broadcasts.FirstOrDefault(b => b.Id == broadcastId);
            if (bc is not null)
            {
                bc.Status = "completed";
                bc.DispatchedCount = dispatchedCount;
                bc.CompletedAtUtc = completedAtUtc;
            }
        }
        return Task.CompletedTask;
    }
}
