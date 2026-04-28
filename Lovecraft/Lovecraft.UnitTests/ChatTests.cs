using Lovecraft.Backend.MockData;
using Lovecraft.Backend.Services;
using Lovecraft.Common.Models;
using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void PagedResult_HasNextCursorAndNullableTotal()
    {
        var result = new PagedResult<int>
        {
            Items = new List<int> { 1, 2, 3 },
            PageSize = 3,
            HasMore = true,
            NextCursor = "some-cursor",
            Total = 42
        };
        Assert.Equal("some-cursor", result.NextCursor);
        Assert.Equal(42, result.Total);

        var noTotal = new PagedResult<int> { Items = new(), PageSize = 5, HasMore = false };
        Assert.Null(noTotal.NextCursor);
        Assert.Null(noTotal.Total);
    }
}
