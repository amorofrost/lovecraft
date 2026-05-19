using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Azure;
using Lovecraft.Backend.Services.Caching;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Users;
using Lovecraft.Common.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests;

/// <summary>
/// Mock-based tests for AzureUserService covering concurrency-safety concerns
/// on IncrementCounterAsync (ETag 412 conflict retry behaviour).
/// </summary>
public class AzureUserServiceIncrementCounterTests
{
    private static UserEntity BuildUser(string userId, int replyCount = 0) => new()
    {
        PartitionKey = UserEntity.GetPartitionKey(userId),
        RowKey = userId,
        Name = "Test",
        ReplyCount = replyCount,
        ETag = new ETag("\"etag-v1\"")
    };

    private static (AzureUserService svc, Mock<TableClient> tc) BuildService(
        Func<int, UserEntity> readUser,
        Queue<Func<UserEntity, object>> updateBehaviors)
    {
        var tc = new Mock<TableClient>();

        tc.Setup(t => t.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue<TableItem>(null!, Mock.Of<Response>()));

        int readCount = 0;
        tc.Setup(t => t.GetEntityAsync<UserEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var u = readUser(readCount);
                readCount++;
                return Response.FromValue(u, Mock.Of<Response>());
            });

        tc.Setup(t => t.UpdateEntityAsync(
                It.IsAny<UserEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .Returns<UserEntity, ETag, TableUpdateMode, CancellationToken>((entity, etag, mode, ct) =>
            {
                if (updateBehaviors.Count == 0)
                    throw new InvalidOperationException("UpdateEntityAsync called more times than expected");
                var behavior = updateBehaviors.Dequeue();
                var result = behavior(entity);
                if (result is Exception ex) throw ex;
                return Task.FromResult((Response)result);
            });

        var tsc = new Mock<TableServiceClient>();
        tsc.Setup(x => x.GetTableClient(TableNames.Users)).Returns(tc.Object);
        tsc.Setup(x => x.GetTableClient(TableNames.UserTelegramIndex)).Returns(tc.Object);

        var svc = new AzureUserService(
            tsc.Object,
            NullLogger<AzureUserService>.Instance,
            new MockAppConfigService(),
            new UserCache());
        return (svc, tc);
    }

    [Fact]
    public async Task IncrementCounterAsync_Success_CallsUpdateOnce()
    {
        var updates = new Queue<Func<UserEntity, object>>();
        updates.Enqueue(_ => Mock.Of<Response>());

        var (svc, tc) = BuildService(_ => BuildUser("1"), updates);

        await svc.IncrementCounterAsync("1", UserCounter.ReplyCount);

        tc.Verify(t => t.UpdateEntityAsync(
            It.IsAny<UserEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IncrementCounterAsync_412Then200_RetriesAndSucceeds()
    {
        var updates = new Queue<Func<UserEntity, object>>();
        updates.Enqueue(_ => new RequestFailedException(412, "Precondition Failed"));
        updates.Enqueue(_ => Mock.Of<Response>());

        var (svc, tc) = BuildService(_ => BuildUser("1"), updates);

        await svc.IncrementCounterAsync("1", UserCounter.ReplyCount);

        tc.Verify(t => t.UpdateEntityAsync(
            It.IsAny<UserEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        tc.Verify(t => t.GetEntityAsync<UserEntity>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task IncrementCounterAsync_RepeatedConflicts_GivesUpAfterMaxAttempts()
    {
        var updates = new Queue<Func<UserEntity, object>>();
        updates.Enqueue(_ => new RequestFailedException(412, "Precondition Failed"));
        updates.Enqueue(_ => new RequestFailedException(412, "Precondition Failed"));
        updates.Enqueue(_ => new RequestFailedException(412, "Precondition Failed"));

        var (svc, tc) = BuildService(_ => BuildUser("1"), updates);

        // After maxAttempts (3) consecutive 412s, the final one propagates.
        var ex = await Assert.ThrowsAsync<RequestFailedException>(
            () => svc.IncrementCounterAsync("1", UserCounter.ReplyCount));
        Assert.Equal(412, ex.Status);

        tc.Verify(t => t.UpdateEntityAsync(
            It.IsAny<UserEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task IncrementCounterAsync_UserNotFound_SwallowsSilently()
    {
        var tc = new Mock<TableClient>();
        tc.Setup(t => t.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue<TableItem>(null!, Mock.Of<Response>()));
        tc.Setup(t => t.GetEntityAsync<UserEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        var tsc = new Mock<TableServiceClient>();
        tsc.Setup(x => x.GetTableClient(TableNames.Users)).Returns(tc.Object);
        tsc.Setup(x => x.GetTableClient(TableNames.UserTelegramIndex)).Returns(tc.Object);

        var svc = new AzureUserService(
            tsc.Object, NullLogger<AzureUserService>.Instance, new MockAppConfigService(), new UserCache());

        // Should not throw.
        await svc.IncrementCounterAsync("missing", UserCounter.ReplyCount);

        tc.Verify(t => t.UpdateEntityAsync(
            It.IsAny<UserEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Tests for AzureUserService.UpdateUserAsync covering field round-trips through JSON columns.
/// </summary>
public class AzureUserServiceUpdateTests
{
    private static UserEntity BuildSeedEntity(string userId) => new()
    {
        PartitionKey = UserEntity.GetPartitionKey(userId),
        RowKey = userId,
        Name = "Seed",
        Age = 25,
        Bio = string.Empty,
        Location = string.Empty,
        Gender = "PreferNotToSay",
        ProfileImage = string.Empty,
        ImagesJson = "[]",
        PromptsJson = "[]",
        PreferencesJson = "{}",
        SettingsJson = "{}",
        FavoriteSongJson = string.Empty,
        StaffRole = "none",
        ETag = new ETag("\"etag-v1\"")
    };

    private static (AzureUserService svc, List<UserEntity> capturedUpdates) BuildService(UserEntity seedEntity)
    {
        var tc = new Mock<TableClient>();

        tc.Setup(t => t.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue<TableItem>(null!, Mock.Of<Response>()));

        tc.Setup(t => t.GetEntityAsync<UserEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(seedEntity, Mock.Of<Response>()));

        var capturedUpdates = new List<UserEntity>();
        tc.Setup(t => t.UpdateEntityAsync(
                It.IsAny<UserEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .Callback<UserEntity, ETag, TableUpdateMode, CancellationToken>((entity, _, _, _) =>
                capturedUpdates.Add(entity))
            .ReturnsAsync(Mock.Of<Response>());

        var tsc = new Mock<TableServiceClient>();
        tsc.Setup(x => x.GetTableClient(TableNames.Users)).Returns(tc.Object);
        tsc.Setup(x => x.GetTableClient(TableNames.UserTelegramIndex)).Returns(tc.Object);

        var svc = new AzureUserService(
            tsc.Object,
            NullLogger<AzureUserService>.Instance,
            new MockAppConfigService(),
            new UserCache());

        return (svc, capturedUpdates);
    }

    [Fact]
    public async Task UpdateUserAsync_RoundTripsPromptsThroughPromptsJson()
    {
        const string userId = "user1";
        var seed = BuildSeedEntity(userId);
        var (svc, capturedUpdates) = BuildService(seed);

        var dto = new UserDto
        {
            Id = userId,
            Name = "Test User",
            Age = 25,
            Bio = string.Empty,
            Location = string.Empty,
            Gender = Gender.PreferNotToSay,
            ProfileImage = string.Empty,
            Images = new List<string>(),
            Preferences = new UserPreferencesDto(),
            Settings = new UserSettingsDto(),
            Prompts = new List<PromptAnswerDto>
            {
                new() { PromptId = "aloevera_song", Answer = "Hometown" },
                new() { PromptId = "looking_for",   Answer = "Someone who travels for shows" }
            }
        };

        var returned = await svc.UpdateUserAsync(userId, dto);

        // Assert: PromptsJson was serialised with both prompt fields
        Assert.Single(capturedUpdates);
        var persisted = capturedUpdates[0];
        Assert.Contains("aloevera_song", persisted.PromptsJson);
        Assert.Contains("Hometown", persisted.PromptsJson);

        // Assert: the returned DTO round-trips both prompts back
        Assert.NotNull(returned.Prompts);
        Assert.Equal(2, returned.Prompts!.Count);

        var song = returned.Prompts.Single(p => p.PromptId == "aloevera_song");
        Assert.Equal("Hometown", song.Answer);

        var looking = returned.Prompts.Single(p => p.PromptId == "looking_for");
        Assert.Equal("Someone who travels for shows", looking.Answer);
    }
}
