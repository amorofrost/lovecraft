using Lovecraft.Common.DTOs.Admin;
using System.Security.Cryptography;

namespace Lovecraft.Backend.Services;

/// <summary>In-memory invites for mock mode (shared static state).</summary>
public class MockEventInviteService : IEventInviteService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, MockRow> ByNormalizedKey = new(StringComparer.Ordinal);

    private sealed class MockRow
    {
        public required string PlainDisplay { get; set; }
        public required string EventId { get; set; }
        public string CampaignLabel { get; set; } = string.Empty;
        public required DateTime ExpiresAtUtc { get; set; }
        public bool Revoked { get; set; }
        public required DateTime CreatedAtUtc { get; set; }
        public int RegistrationCount { get; set; }
        public int EventAttendanceClaimCount { get; set; }
    }

    public Task<EventInviteValidationResult?> ValidatePlainCodeAsync(string plainCode, CancellationToken cancellationToken = default)
    {
        var key = EventInviteNormalizer.Normalize(plainCode);
        lock (Gate)
        {
            if (string.IsNullOrEmpty(key) || !ByNormalizedKey.TryGetValue(key, out var row))
                return Task.FromResult<EventInviteValidationResult?>(null);
            if (row.Revoked || row.ExpiresAtUtc < DateTime.UtcNow)
                return Task.FromResult<EventInviteValidationResult?>(null);
            return Task.FromResult<EventInviteValidationResult?>(new EventInviteValidationResult(row.EventId, key));
        }
    }

    public Task<(string PlainCode, DateTime ExpiresAtUtc)> CreateOrRotateInviteAsync(
        string eventId,
        DateTime expiresAtUtc,
        string? plainCodeOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (EventInviteHelpers.IsCampaignEventId(eventId))
            throw new ArgumentException("Use CreateCampaignInviteAsync for campaign ids.", nameof(eventId));

        lock (Gate)
        {
            foreach (var kv in ByNormalizedKey.ToList())
            {
                if (kv.Value.EventId == eventId && !kv.Value.Revoked)
                    kv.Value.Revoked = true;
            }

            if (!string.IsNullOrWhiteSpace(plainCodeOverride))
            {
                var plain = plainCodeOverride.Trim();
                var key = EventInviteNormalizer.Normalize(plain);
                if (string.IsNullOrEmpty(key))
                    throw new ArgumentException("PlainCode cannot be empty.", nameof(plainCodeOverride));

                if (ByNormalizedKey.TryGetValue(key, out var existing))
                {
                    if (!existing.Revoked && !string.Equals(existing.EventId, eventId, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Invite code '{key}' is already in use.");
                }

                ByNormalizedKey[key] = new MockRow
                {
                    PlainDisplay = plain,
                    EventId = eventId,
                    ExpiresAtUtc = expiresAtUtc,
                    Revoked = false,
                    CreatedAtUtc = DateTime.UtcNow,
                    RegistrationCount = 0,
                    EventAttendanceClaimCount = 0,
                };

                return Task.FromResult((plain, expiresAtUtc));
            }

            var autoPlain = GenerateUniquePlainCodeLocked();
            var autoKey = EventInviteNormalizer.Normalize(autoPlain);
            ByNormalizedKey[autoKey] = new MockRow
            {
                PlainDisplay = autoPlain,
                EventId = eventId,
                ExpiresAtUtc = expiresAtUtc,
                Revoked = false,
                CreatedAtUtc = DateTime.UtcNow,
                RegistrationCount = 0,
                EventAttendanceClaimCount = 0,
            };
            return Task.FromResult((autoPlain, expiresAtUtc));
        }
    }

    public Task<(string PlainCode, DateTime ExpiresAtUtc)> CreateCampaignInviteAsync(
        string campaignId,
        string? campaignLabel,
        DateTime expiresAtUtc,
        string? plainCodeOverride,
        CancellationToken cancellationToken = default)
    {
        var id = campaignId.Trim();
        if (!EventInviteHelpers.IsCampaignEventId(id))
            throw new ArgumentException("CampaignId must be a negative integer string (e.g. -1).", nameof(campaignId));

        lock (Gate)
        {
            string plain;
            if (!string.IsNullOrWhiteSpace(plainCodeOverride))
            {
                plain = plainCodeOverride.Trim();
                var key = EventInviteNormalizer.Normalize(plain);
                if (string.IsNullOrEmpty(key))
                    throw new ArgumentException("PlainCode cannot be empty.", nameof(plainCodeOverride));
                if (ByNormalizedKey.ContainsKey(key))
                    throw new InvalidOperationException($"Invite code '{key}' already exists.");
            }
            else
            {
                plain = GenerateUniquePlainCodeLocked();
            }

            var rk = EventInviteNormalizer.Normalize(plain);
            ByNormalizedKey[rk] = new MockRow
            {
                PlainDisplay = plain,
                EventId = id,
                CampaignLabel = campaignLabel?.Trim() ?? string.Empty,
                ExpiresAtUtc = expiresAtUtc,
                Revoked = false,
                CreatedAtUtc = DateTime.UtcNow,
                RegistrationCount = 0,
                EventAttendanceClaimCount = 0,
            };
            return Task.FromResult((plain, expiresAtUtc));
        }
    }

    public Task<IReadOnlyList<EventInviteAdminDto>> ListInvitesAsync(CancellationToken cancellationToken = default)
    {
        lock (Gate)
        {
            var list = ByNormalizedKey.Values
                .Select(r => new EventInviteAdminDto(
                    EventInviteNormalizer.Normalize(r.PlainDisplay),
                    r.EventId,
                    string.IsNullOrWhiteSpace(r.CampaignLabel) ? null : r.CampaignLabel,
                    r.ExpiresAtUtc,
                    r.Revoked,
                    r.CreatedAtUtc,
                    r.RegistrationCount,
                    r.EventAttendanceClaimCount))
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<EventInviteAdminDto>>(list);
        }
    }

    public Task IncrementRegistrationCountAsync(string plainCode, CancellationToken cancellationToken = default)
    {
        var key = EventInviteNormalizer.Normalize(plainCode);
        lock (Gate)
        {
            if (ByNormalizedKey.TryGetValue(key, out var row))
                row.RegistrationCount++;
        }
        return Task.CompletedTask;
    }

    public Task IncrementEventAttendanceClaimCountAsync(string plainCode, CancellationToken cancellationToken = default)
    {
        var key = EventInviteNormalizer.Normalize(plainCode);
        lock (Gate)
        {
            if (ByNormalizedKey.TryGetValue(key, out var row))
                row.EventAttendanceClaimCount++;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAllInvitesForEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        lock (Gate)
        {
            var keys = ByNormalizedKey.Where(kv => kv.Value.EventId == eventId).Select(kv => kv.Key).ToList();
            foreach (var k in keys)
                ByNormalizedKey.Remove(k);
        }
        return Task.CompletedTask;
    }

    private static string GenerateUniquePlainCodeLocked()
    {
        for (var i = 0; i < 64; i++)
        {
            var plain = GenerateReadablePlainCode();
            var key = EventInviteNormalizer.Normalize(plain);
            if (!ByNormalizedKey.ContainsKey(key))
                return plain;
        }
        throw new InvalidOperationException("Could not allocate a unique invite code.");
    }

    private static string GenerateReadablePlainCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var r = new byte[8];
        RandomNumberGenerator.Fill(r);
        var s = new char[8];
        for (var i = 0; i < 8; i++)
            s[i] = chars[r[i] % chars.Length];
        return $"ALOE-{new string(s)}";
    }
}
