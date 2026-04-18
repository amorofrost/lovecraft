using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using System.Security.Cryptography;

namespace Lovecraft.Backend.Services.Azure;

public class AzureEventInviteService : IEventInviteService
{
    private readonly TableClient _table;
    private readonly string _pepper;
    private readonly ILogger<AzureEventInviteService> _logger;

    public AzureEventInviteService(
        TableServiceClient tableServiceClient,
        IConfiguration configuration,
        ILogger<AzureEventInviteService> logger)
    {
        _logger = logger;
        _pepper = configuration["JWT_SECRET"]
            ?? configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT_SECRET or Jwt:Secret required for event invite hashing");
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
}
