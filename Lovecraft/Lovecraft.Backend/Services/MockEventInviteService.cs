using System.Security.Cryptography;

namespace Lovecraft.Backend.Services;

/// <summary>In-memory invites for mock mode (shared static state).</summary>
public class MockEventInviteService : IEventInviteService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, MockRow> ByHash = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<string>> HashesByEvent = new(StringComparer.Ordinal);

    private sealed class MockRow
    {
        public required string EventId { get; init; }
        public required DateTime ExpiresAtUtc { get; init; }
        public bool Revoked { get; set; }
        public required string RowKey { get; init; }
    }

    private readonly string _pepper;

    public MockEventInviteService(IConfiguration configuration)
    {
        _pepper = configuration["JWT_SECRET"]
            ?? configuration["Jwt:Secret"]
            ?? configuration["JWT_SECRET_KEY"]
            ?? "mock-event-invite-pepper-change-me-32chars!!";
    }

    public Task<EventInviteValidationResult?> ValidatePlainCodeAsync(string plainCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainCode))
            return Task.FromResult<EventInviteValidationResult?>(null);

        var hash = EventInviteHasher.Hash(plainCode, _pepper);
        lock (Gate)
        {
            if (!ByHash.TryGetValue(hash, out var row) || row.Revoked || row.ExpiresAtUtc < DateTime.UtcNow)
                return Task.FromResult<EventInviteValidationResult?>(null);

            return Task.FromResult<EventInviteValidationResult?>(new EventInviteValidationResult(row.EventId, row.RowKey));
        }
    }

    public Task<(string PlainCode, DateTime ExpiresAtUtc)> CreateOrRotateInviteAsync(
        string eventId,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        var plain = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        lock (Gate)
        {
            if (HashesByEvent.TryGetValue(eventId, out var oldHashes))
            {
                foreach (var h in oldHashes)
                {
                    if (ByHash.TryGetValue(h, out var r))
                        r.Revoked = true;
                }
                oldHashes.Clear();
            }
            else
            {
                HashesByEvent[eventId] = new List<string>();
            }

            var hash = EventInviteHasher.Hash(plain, _pepper);
            ByHash[hash] = new MockRow
            {
                EventId = eventId,
                ExpiresAtUtc = expiresAtUtc,
                Revoked = false,
                RowKey = hash,
            };
            HashesByEvent[eventId].Add(hash);
        }

        return Task.FromResult((plain, expiresAtUtc));
    }
}
