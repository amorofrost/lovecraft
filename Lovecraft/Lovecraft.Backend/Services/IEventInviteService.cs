namespace Lovecraft.Backend.Services;

public record EventInviteValidationResult(string EventId, string InviteRowKey);

public interface IEventInviteService
{
    /// <summary>Validates a plaintext invite; returns null if invalid, expired, or revoked.</summary>
    Task<EventInviteValidationResult?> ValidatePlainCodeAsync(string plainCode, CancellationToken cancellationToken = default);

    /// <summary>Creates a new invite for the event and revokes any previous non-revoked invites for that event. Returns the plaintext code once.</summary>
    Task<(string PlainCode, DateTime ExpiresAtUtc)> CreateOrRotateInviteAsync(string eventId, DateTime expiresAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Removes all invite rows for the event (e.g. before deleting the event).</summary>
    Task DeleteAllInvitesForEventAsync(string eventId, CancellationToken cancellationToken = default);
}
