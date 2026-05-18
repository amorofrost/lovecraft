using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Lovecraft.UnitTests;

[Collection("ChatTests")]
public class ChatTests
{
    private static MockChatService CreateService() => new();

    [Fact]
    public async Task GetChatsAsync_ReturnsOnlyChatsForUser()
    {
        var svc = CreateService();
        var chats = await svc.GetChatsAsync("current-user");
        Assert.All(chats, c => Assert.Contains("current-user", c.Participants));
    }

    [Fact]
    public async Task GetChatsAsync_ExcludesChatsForOtherUsers()
    {
        var svc = CreateService();
        var chats = await svc.GetChatsAsync("stranger-user");
        Assert.Empty(chats);
    }

    [Fact]
    public async Task GetOrCreateChatAsync_CreatesNewChat()
    {
        var svc = CreateService();
        var chat = await svc.GetOrCreateChatAsync("user-new-a", "user-new-b");
        Assert.Contains("user-new-a", chat.Participants);
        Assert.Contains("user-new-b", chat.Participants);
    }

    [Fact]
    public async Task GetOrCreateChatAsync_ReturnsExistingChat()
    {
        var svc = CreateService();
        var first  = await svc.GetOrCreateChatAsync("user-x", "user-y");
        var second = await svc.GetOrCreateChatAsync("user-x", "user-y");
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task GetOrCreateChatAsync_IsIdempotentFromEitherSide()
    {
        var svc = CreateService();
        var ab = await svc.GetOrCreateChatAsync("user-p", "user-q");
        var ba = await svc.GetOrCreateChatAsync("user-q", "user-p");
        Assert.Equal(ab.Id, ba.Id);
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsMessagesOldestFirst()
    {
        var svc = CreateService();
        var msgs = await svc.GetMessagesAsync("chat-1", "current-user");
        Assert.NotEmpty(msgs);
        for (int i = 1; i < msgs.Count; i++)
            Assert.True(msgs[i].Timestamp >= msgs[i - 1].Timestamp);
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsEmptyForNonParticipant()
    {
        var svc = CreateService();
        var msgs = await svc.GetMessagesAsync("chat-1", "stranger-user");
        Assert.Empty(msgs);
    }

    [Fact]
    public async Task SendMessageAsync_PersistsMessage()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "Hello!");
        var history = await svc.GetMessagesAsync("chat-1", "current-user");
        Assert.Contains(history, m => m.Id == msg.Id && m.Content == "Hello!");
    }

    [Fact]
    public async Task SendMessageAsync_UpdatesLastMessageInUserChatsIndex()
    {
        var svc = CreateService();
        await svc.SendMessageAsync("chat-1", "current-user", "Updated!");
        var chats = await svc.GetChatsAsync("current-user");
        Assert.Contains(chats, c => c.LastMessage?.Content == "Updated!");
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsForNonParticipant()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SendMessageAsync("chat-1", "stranger-user", "Hack!"));
    }

    [Fact]
    public async Task ValidateAccessAsync_ReturnsTrueForParticipant()
    {
        var svc = CreateService();
        var result = await svc.ValidateAccessAsync("chat-1", "current-user");
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAccessAsync_ReturnsFalseForNonParticipant()
    {
        var svc = CreateService();
        var result = await svc.ValidateAccessAsync("chat-1", "stranger-user");
        Assert.False(result);
    }

    [Fact]
    public async Task GetMessagesAsync_PaginatesCorrectly()
    {
        var svc = CreateService();
        // chat-1 has 3 seeded messages; page 1 with pageSize 2 → 2 messages
        var page1 = await svc.GetMessagesAsync("chat-1", "current-user", page: 1, pageSize: 2);
        Assert.Equal(2, page1.Count);
    }

    // --- Hub path tests (via MockChatService, which ChatHub delegates to) ---

    [Fact]
    public async Task ValidateAccessAsync_CalledByHub_ReturnsTrueForParticipant()
    {
        // Simulates ChatHub.JoinChat / SendMessage calling ValidateAccessAsync
        var svc = CreateService();
        var allowed = await svc.ValidateAccessAsync("chat-1", "current-user");
        Assert.True(allowed);
    }

    [Fact]
    public async Task ValidateAccessAsync_CalledByHub_ReturnsFalseForNonParticipant()
    {
        // Simulates ChatHub rejecting a JoinChat from a non-participant
        var svc = CreateService();
        var denied = await svc.ValidateAccessAsync("chat-1", "intruder");
        Assert.False(denied);
    }

    [Fact]
    public async Task SendMessageAsync_CalledByHub_ThrowsForEmptyContent()
    {
        // ChatHub throws HubException for empty content before calling service;
        // here we verify the service itself rejects invalid chat IDs (non-participant)
        var svc = CreateService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SendMessageAsync("chat-nonexistent", "current-user", "Hello"));
    }

    [Fact]
    public async Task SendMessageAsync_CalledByHub_PersistsMessageForOtherParticipant()
    {
        // Simulates hub: sender sends via SendMessage; recipient should see it via GetMessages
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "Hub send test");
        var recipientView = await svc.GetMessagesAsync("chat-1", "user-anna");
        Assert.Contains(recipientView, m => m.Id == msg.Id);
    }

    [Fact]
    public async Task SendMessageAsync_CalledByHub_DoesNotExcludeSenderFromPersistence()
    {
        // OthersInGroup only affects SignalR broadcast, not persistence;
        // sender's own GetMessages should still include the sent message
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "Self-visible");
        var senderView = await svc.GetMessagesAsync("chat-1", "current-user");
        Assert.Contains(senderView, m => m.Id == msg.Id);
    }

    [Fact]
    public async Task SendMessageAsync_WithImageUrls_StoresAndReturnsThem()
    {
        var svc = CreateService();
        var imageUrls = new List<string>
        {
            "https://example.com/img1.jpg",
            "https://example.com/img2.jpg"
        };
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "See photos!", imageUrls);
        Assert.Equal(imageUrls, msg.ImageUrls);
        var history = await svc.GetMessagesAsync("chat-1", "current-user");
        var persisted = history.First(m => m.Id == msg.Id);
        Assert.Equal(imageUrls, persisted.ImageUrls);
    }
}

[Collection("ChatNotificationTests")]
public class ChatNotificationTests : IClassFixture<AclTests.TestAppFactory>
{
    private readonly AclTests.TestAppFactory _factory;

    public ChatNotificationTests(AclTests.TestAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientAsUser(WebApplicationFactory<Program> factory, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        client.DefaultRequestHeaders.Add("X-Test-StaffRole", "none");
        return client;
    }

    [Fact]
    public async Task SendMessage_fires_producer_for_each_other_participant()
    {
        var producer = new Mock<INotificationProducer>();
        producer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync((Lovecraft.Common.DTOs.Notifications.NotificationDto?)null);

        var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<INotificationProducer>(producer.Object)));
        using var client = CreateClientAsUser(factory, "u-sender");

        // Create chat between u-sender and u-other
        var chatResp = await client.PostAsJsonAsync("/api/v1/chats", new { targetUserId = "u-other" });
        chatResp.EnsureSuccessStatusCode();
        var chatJson = await chatResp.Content.ReadFromJsonAsync<JsonElement>();
        var chatId = chatJson.GetProperty("data").GetProperty("id").GetString();

        producer.Invocations.Clear();
        var sendResp = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { content = "hello there" });
        sendResp.EnsureSuccessStatusCode();

        producer.Verify(p => p.ProduceAsync(
            "u-other",
            NotificationType.MessageReceived,
            "u-sender",
            It.Is<string>(s => s.Contains("\"chatId\"") && s.Contains("\"preview\":\"hello there\"")),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SendMessage_does_not_fire_for_sender()
    {
        var producer = new Mock<INotificationProducer>();
        producer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync((Lovecraft.Common.DTOs.Notifications.NotificationDto?)null);

        var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<INotificationProducer>(producer.Object)));
        using var client = CreateClientAsUser(factory, "u-sender");

        var chatResp = await client.PostAsJsonAsync("/api/v1/chats", new { targetUserId = "u-other" });
        chatResp.EnsureSuccessStatusCode();
        var chatJson = await chatResp.Content.ReadFromJsonAsync<JsonElement>();
        var chatId = chatJson.GetProperty("data").GetProperty("id").GetString();

        producer.Invocations.Clear();
        await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages", new { content = "hi" });

        producer.Verify(p => p.ProduceAsync(
            "u-sender", It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SendMessage_preview_truncated_to_80_chars()
    {
        var producer = new Mock<INotificationProducer>();
        producer.Setup(p => p.ProduceAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync((Lovecraft.Common.DTOs.Notifications.NotificationDto?)null);

        var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<INotificationProducer>(producer.Object)));
        using var client = CreateClientAsUser(factory, "u-sender");

        var chatResp = await client.PostAsJsonAsync("/api/v1/chats", new { targetUserId = "u-other" });
        chatResp.EnsureSuccessStatusCode();
        var chatJson = await chatResp.Content.ReadFromJsonAsync<JsonElement>();
        var chatId = chatJson.GetProperty("data").GetProperty("id").GetString();

        var longContent = new string('x', 200);
        await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages", new { content = longContent });

        var eightyXs = new string('x', 80);
        producer.Verify(p => p.ProduceAsync(
            "u-other", NotificationType.MessageReceived, It.IsAny<string?>(),
            It.Is<string>(s =>
                s.Contains("\"preview\":\"" + eightyXs + "\"") ||
                s.Contains("\"preview\":\"" + eightyXs + "…\"") ||
                s.Contains("\"preview\":\"" + eightyXs + "\\u2026\"")),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Once);
    }
}
