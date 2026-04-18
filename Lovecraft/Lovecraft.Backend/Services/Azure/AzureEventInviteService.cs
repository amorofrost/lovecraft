using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Auth;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using System.Security.Cryptography;

namespace Lovecraft.Backend.Services.Azure;

public class AzureEventInviteService : IEventInviteService
{
    private readonly TableClient _table;
    private readonly string _pepper;
    private readonly ILogger<AzureEventInviteService> _logger;

    /// <summary>
    /// Uses <see cref="JwtSettings.SecretKey"/> as the HMAC pepper so it always matches
    /// the JWT signing key configured via <c>JWT_SECRET_KEY</c>.
    /// </summary>
    public AzureEventInviteService(
        TableServiceClient tableServiceClient,
        JwtSettings jwtSettings,
        ILogger<AzureEventInviteService> logger)
    {
        _logger = logger;
        if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
            throw new InvalidOperationException(
                "JwtSettings.SecretKey is empty — set JWT_SECRET_KEY (required for JWT and event invite hashing).");
        _pepper = jwtSettings.SecretKey;
        _table = tableServiceClient.GetTableClient(TableNames.EventInvites);
        _table.CreateIfNotExistsAsync().GetAwaiter().GetResult();
    }

    public async Task<EventInviteValidationResult?> ValidatePlainCodeAsync(string plainCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainCode))
            return null;

        var hash = EventInviteHasher.Hash(plainCode, _pepper);
        try
        {
            var entity = await _table.GetEntityAsync<EventInviteEntity>(
                EventInviteEntity.PartitionValue,
                hash,
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
        var plain = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        await RevokeExistingForEventAsync(eventId, cancellationToken).ConfigureAwait(false);

        var hash = EventInviteHasher.Hash(plain, _pepper);
        var row = new EventInviteEntity
        {
            PartitionKey = EventInviteEntity.PartitionValue,
            RowKey = hash,
            EventId = eventId,
            ExpiresAtUtc = expiresAtUtc,
            Revoked = false,
            CreatedAtUtc = DateTime.UtcNow,
        };

        await _table.UpsertEntityAsync(row, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
        return (plain, expiresAtUtc);
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
}
