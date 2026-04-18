using System.Globalization;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Admin;
using System.Security.Cryptography;

namespace Lovecraft.Backend.Services.Azure;

public class AzureEventInviteService : IEventInviteService
{
    private readonly TableClient _table;
    private readonly ILogger<AzureEventInviteService> _logger;

    public AzureEventInviteService(TableServiceClient tableServiceClient, ILogger<AzureEventInviteService> logger)
    {
        _logger = logger;
        _table = tableServiceClient.GetTableClient(TableNames.EventInvites);
        _table.CreateIfNotExistsAsync().GetAwaiter().GetResult();
    }

    public async Task<EventInviteValidationResult?> ValidatePlainCodeAsync(string plainCode, CancellationToken cancellationToken = default)
    {
        var key = EventInviteNormalizer.Normalize(plainCode);
        if (string.IsNullOrEmpty(key))
            return null;

        try
        {
            var entity = await _table.GetEntityAsync<EventInviteEntity>(
                EventInviteEntity.PartitionValue,
                key,
                cancellationToken: cancellationToken);

            var e = entity.Value;
            if (e.Revoked || e.ExpiresAtUtc < DateTime.UtcNow)
                return null;

            return new EventInviteValidationResult(e.EventId, e.RowKey);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<(string PlainCode, DateTime ExpiresAtUtc)> CreateOrRotateInviteAsync(
        string eventId,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (EventInviteHelpers.IsCampaignEventId(eventId))
            throw new ArgumentException("Use CreateCampaignInviteAsync for campaign (negative) ids.", nameof(eventId));

        var plain = await GenerateUniquePlainCodeAsync(cancellationToken).ConfigureAwait(false);
        var key = EventInviteNormalizer.Normalize(plain);

        await RevokeExistingForEventAsync(eventId, cancellationToken).ConfigureAwait(false);

        var row = new EventInviteEntity
        {
            PartitionKey = EventInviteEntity.PartitionValue,
            RowKey = key,
            PlainCode = key,
            EventId = eventId,
            CampaignLabel = string.Empty,
            ExpiresAtUtc = expiresAtUtc,
            Revoked = false,
            CreatedAtUtc = DateTime.UtcNow,
            RegistrationCount = 0,
            EventAttendanceClaimCount = 0,
        };

        await _table.AddEntityAsync(row, cancellationToken).ConfigureAwait(false);
        return (plain, expiresAtUtc);
    }

    public async Task<(string PlainCode, DateTime ExpiresAtUtc)> CreateCampaignInviteAsync(
        string campaignId,
        string? campaignLabel,
        DateTime expiresAtUtc,
        string? plainCodeOverride,
        CancellationToken cancellationToken = default)
    {
        var id = campaignId.Trim();
        if (!EventInviteHelpers.IsCampaignEventId(id))
            throw new ArgumentException("CampaignId must be a negative integer string (e.g. -1, -2).", nameof(campaignId));

        string plain;
        if (!string.IsNullOrWhiteSpace(plainCodeOverride))
        {
            plain = plainCodeOverride.Trim();
            var key = EventInviteNormalizer.Normalize(plain);
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("PlainCode cannot be empty.", nameof(plainCodeOverride));
            try
            {
                await _table.GetEntityAsync<EventInviteEntity>(EventInviteEntity.PartitionValue, key, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidOperationException($"Invite code '{key}' already exists.");
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // free
            }
        }
        else
        {
            plain = await GenerateUniquePlainCodeAsync(cancellationToken).ConfigureAwait(false);
        }

        var rk = EventInviteNormalizer.Normalize(plain);
        var row = new EventInviteEntity
        {
            PartitionKey = EventInviteEntity.PartitionValue,
            RowKey = rk,
            PlainCode = rk,
            EventId = id,
            CampaignLabel = campaignLabel?.Trim() ?? string.Empty,
            ExpiresAtUtc = expiresAtUtc,
            Revoked = false,
            CreatedAtUtc = DateTime.UtcNow,
            RegistrationCount = 0,
            EventAttendanceClaimCount = 0,
        };

        await _table.AddEntityAsync(row, cancellationToken).ConfigureAwait(false);
        return (plain, expiresAtUtc);
    }

    public async Task<IReadOnlyList<EventInviteAdminDto>> ListInvitesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<EventInviteEntity>();
        await foreach (var e in _table.QueryAsync<EventInviteEntity>(
            filter: $"PartitionKey eq '{EventInviteEntity.PartitionValue}'",
            cancellationToken: cancellationToken))
        {
            list.Add(e);
        }

        return list
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(ToDto)
            .ToList();
    }

    public async Task IncrementRegistrationCountAsync(string plainCode, CancellationToken cancellationToken = default)
    {
        var key = EventInviteNormalizer.Normalize(plainCode);
        if (string.IsNullOrEmpty(key))
            return;

        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var r = await _table.GetEntityAsync<EventInviteEntity>(
                    EventInviteEntity.PartitionValue,
                    key,
                    cancellationToken: cancellationToken);
                var e = r.Value;
                e.RegistrationCount++;
                await _table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxAttempts - 1)
            {
                await Task.Delay(Random.Shared.Next(5, 40), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task IncrementEventAttendanceClaimCountAsync(string plainCode, CancellationToken cancellationToken = default)
    {
        var key = EventInviteNormalizer.Normalize(plainCode);
        if (string.IsNullOrEmpty(key))
            return;

        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var r = await _table.GetEntityAsync<EventInviteEntity>(
                    EventInviteEntity.PartitionValue,
                    key,
                    cancellationToken: cancellationToken);
                var e = r.Value;
                e.EventAttendanceClaimCount++;
                await _table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return;
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxAttempts - 1)
            {
                await Task.Delay(Random.Shared.Next(5, 40), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DeleteAllInvitesForEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        var escaped = eventId.Replace("'", "''");
        var toDelete = new List<EventInviteEntity>();
        await foreach (var e in _table.QueryAsync<EventInviteEntity>(
            filter: $"PartitionKey eq '{EventInviteEntity.PartitionValue}' and EventId eq '{escaped}'",
            cancellationToken: cancellationToken))
        {
            toDelete.Add(e);
        }

        foreach (var e in toDelete)
        {
            try
            {
                await _table.DeleteEntityAsync(e.PartitionKey, e.RowKey, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(ex, "Failed to delete invite row {RowKey} for event {EventId}", e.RowKey, eventId);
            }
        }
    }

    private async Task RevokeExistingForEventAsync(string eventId, CancellationToken cancellationToken)
    {
        var toRevoke = new List<EventInviteEntity>();
        await foreach (var e in _table.QueryAsync<EventInviteEntity>(
            filter: $"PartitionKey eq '{EventInviteEntity.PartitionValue}' and EventId eq '{eventId.Replace("'", "''")}'",
            cancellationToken: cancellationToken))
        {
            if (!e.Revoked)
                toRevoke.Add(e);
        }

        foreach (var e in toRevoke)
        {
            e.Revoked = true;
            try
            {
                await _table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(ex, "Failed to revoke invite row {RowKey} for event {EventId}", e.RowKey, eventId);
            }
        }
    }

    private async Task<string> GenerateUniquePlainCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var plain = GenerateReadablePlainCode();
            var key = EventInviteNormalizer.Normalize(plain);
            try
            {
                await _table.GetEntityAsync<EventInviteEntity>(
                    EventInviteEntity.PartitionValue,
                    key,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return plain;
            }
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

    private static EventInviteAdminDto ToDto(EventInviteEntity e) =>
        new(
            string.IsNullOrEmpty(e.PlainCode) ? e.RowKey : e.PlainCode,
            e.EventId,
            string.IsNullOrWhiteSpace(e.CampaignLabel) ? null : e.CampaignLabel,
            e.ExpiresAtUtc,
            e.Revoked,
            e.CreatedAtUtc,
            e.RegistrationCount,
            e.EventAttendanceClaimCount);
}
