using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging;

namespace Lovecraft.Backend.Services.Azure;

public class AzureNotificationPreferenceService : INotificationPreferenceService
{
    private readonly TableClient _table;
    private readonly ILogger<AzureNotificationPreferenceService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AzureNotificationPreferenceService(TableClient table, ILogger<AzureNotificationPreferenceService> logger)
    {
        _table = table;
        _logger = logger;
    }

    public async Task<NotificationPreferencesDto> GetPreferencesAsync(string userId)
    {
        try
        {
            var entity = await _table.GetEntityAsync<NotificationPreferencesEntity>(userId, "INDEX");
            return FromEntity(entity.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return MockNotificationPreferenceService.BuildDefaults();
        }
    }

    public async Task<NotificationPreferencesDto> UpdatePreferencesAsync(string userId, NotificationPreferencesDto prefs)
    {
        var entity = new NotificationPreferencesEntity
        {
            PartitionKey = userId,
            RowKey = "INDEX",
            MatrixJson = JsonSerializer.Serialize(prefs.Matrix, JsonOpts),
            FrequencyJson = JsonSerializer.Serialize(prefs.Frequency, JsonOpts),
            DailyDigestHourUtc = prefs.DailyDigestHourUtc,
            Mute = prefs.Mute,
            MutedUntilUtc = prefs.MutedUntilUtc,
        };
        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        return prefs;
    }

    private static NotificationPreferencesDto FromEntity(NotificationPreferencesEntity e)
    {
        var dto = MockNotificationPreferenceService.BuildDefaults();
        try
        {
            var matrix = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, bool>>>(e.MatrixJson, JsonOpts);
            if (matrix is not null) dto.Matrix = matrix;
        }
        catch { /* fall back to defaults */ }
        try
        {
            var freq = JsonSerializer.Deserialize<Dictionary<string, NotificationFrequency>>(e.FrequencyJson, JsonOpts);
            if (freq is not null) dto.Frequency = freq;
        }
        catch { /* fall back to defaults */ }
        dto.DailyDigestHourUtc = e.DailyDigestHourUtc;
        dto.Mute = e.Mute;
        dto.MutedUntilUtc = e.MutedUntilUtc;
        return dto;
    }
}
