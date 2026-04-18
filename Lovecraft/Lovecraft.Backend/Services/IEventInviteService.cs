using Lovecraft.Common.DTOs.Admin;

namespace Lovecraft.Backend.Services;

public record EventInviteValidationResult(string EventId, string NormalizedPlainCode);

public interface IEventInviteService
{
    /// <summary>Validates a plaintext invite; returns null if invalid, expired, or revoked.</summary>
    Task<EventInviteValidationResult?> ValidatePlainCodeAsync(string plainCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new invite for the event and revokes previous non-revoked invites for that event.
    /// Returns the plaintext code (stored as-is in the table).
    /// </summary>
    Task<(string PlainCode, DateTime ExpiresAtUtc)> CreateOrRotateInviteAsync(string eventId, DateTime expiresAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Non-event campaign invite (negative EventId). Does not revoke other codes.</summary>
    Task<(string PlainCode, DateTime ExpiresAtUtc)> CreateCampaignInviteAsync(
        string campaignId,
        string? campaignLabel,
        DateTime expiresAtUtc,
        string? plainCodeOverride,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventInviteAdminDto>> ListInvitesAsync(CancellationToken cancellationToken = default);

    /// <summary>After a new account is created with this invite code.</summary>
    Task IncrementRegistrationCountAsync(string plainCode, CancellationToken cancellationToken = default);

    /// <summary>When an existing user registers for an event using this invite code.</summary>
    Task IncrementEventAttendanceClaimCountAsync(string plainCode, CancellationToken cancellationToken = default);

    /// <summary>Removes all invite rows for the event (e.g. before deleting the event).</summary>
    Task DeleteAllInvitesForEventAsync(string eventId, CancellationToken cancellationToken = default);
}
