using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

public class NotificationPreferenceServiceTests
{
    [Fact]
    public async Task Get_returns_defaults_when_no_row_exists()
    {
        MockDataStore.NotificationPreferences.Clear();
        var svc = new MockNotificationPreferenceService();

        var prefs = await svc.GetPreferencesAsync("user-new");

        // 9 types in matrix, each with 4 channels
        Assert.Equal(9, prefs.Matrix.Count);
        foreach (var kvp in prefs.Matrix)
        {
            Assert.True(kvp.Value["inApp"], $"inApp should default true for {kvp.Key}");
            Assert.False(kvp.Value["telegram"]);
            Assert.False(kvp.Value["webPush"]);
            Assert.False(kvp.Value["email"]);
        }
        Assert.Equal(NotificationFrequency.Immediate, prefs.Frequency["inApp"]);
        Assert.Equal(NotificationFrequency.Immediate, prefs.Frequency["telegram"]);
        Assert.Equal(NotificationFrequency.Immediate, prefs.Frequency["webPush"]);
        Assert.Equal(NotificationFrequency.Daily, prefs.Frequency["email"]);
        Assert.Equal(9, prefs.DailyDigestHourUtc);
        Assert.False(prefs.Mute);
        Assert.Null(prefs.MutedUntilUtc);
    }

    [Fact]
    public async Task Update_then_Get_round_trips()
    {
        MockDataStore.NotificationPreferences.Clear();
        var svc = new MockNotificationPreferenceService();

        var prefs = await svc.GetPreferencesAsync("user-1");
        prefs.Matrix["likeReceived"]["telegram"] = true;
        prefs.DailyDigestHourUtc = 20;

        await svc.UpdatePreferencesAsync("user-1", prefs);
        var loaded = await svc.GetPreferencesAsync("user-1");

        Assert.True(loaded.Matrix["likeReceived"]["telegram"]);
        Assert.Equal(20, loaded.DailyDigestHourUtc);
    }

    [Fact]
    public async Task Update_isolates_users()
    {
        MockDataStore.NotificationPreferences.Clear();
        var svc = new MockNotificationPreferenceService();

        var aPrefs = await svc.GetPreferencesAsync("user-a");
        aPrefs.Mute = true;
        await svc.UpdatePreferencesAsync("user-a", aPrefs);

        var bPrefs = await svc.GetPreferencesAsync("user-b");
        Assert.False(bPrefs.Mute);
    }
}

public class AzureNotificationPreferenceServiceTests
{
    [Fact]
    public async Task Get_returns_defaults_when_row_missing()
    {
        var table = new Mock<TableClient>();
        table.Setup(t => t.GetEntityAsync<NotificationPreferencesEntity>(
                "user-1", "INDEX", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "not found"));

        var svc = new AzureNotificationPreferenceService(table.Object, NullLogger<AzureNotificationPreferenceService>.Instance);

        var prefs = await svc.GetPreferencesAsync("user-1");

        Assert.True(prefs.Matrix["likeReceived"]["inApp"]);
        Assert.False(prefs.Matrix["likeReceived"]["telegram"]);
        Assert.Equal(9, prefs.DailyDigestHourUtc);
    }

    [Fact]
    public async Task Update_serializes_matrix_and_frequency()
    {
        var table = new Mock<TableClient>();
        NotificationPreferencesEntity? upserted = null;
        table.Setup(t => t.UpsertEntityAsync(
                It.IsAny<NotificationPreferencesEntity>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()))
            .Callback<NotificationPreferencesEntity, TableUpdateMode, CancellationToken>((e, _, _) => upserted = e)
            .ReturnsAsync(new Mock<Response>().Object);

        var svc = new AzureNotificationPreferenceService(table.Object, NullLogger<AzureNotificationPreferenceService>.Instance);

        var prefs = MockNotificationPreferenceService.BuildDefaults();
        prefs.DailyDigestHourUtc = 18;
        prefs.Matrix["likeReceived"]["telegram"] = true;

        await svc.UpdatePreferencesAsync("user-2", prefs);

        Assert.NotNull(upserted);
        Assert.Equal("user-2", upserted!.PartitionKey);
        Assert.Equal("INDEX", upserted.RowKey);
        Assert.Equal(18, upserted.DailyDigestHourUtc);
        Assert.Contains("\"telegram\":true", upserted.MatrixJson);
    }
}
