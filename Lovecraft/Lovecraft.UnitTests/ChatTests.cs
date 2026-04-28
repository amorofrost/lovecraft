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
    public async Task GetMessagesAsync_ReturnsNewestFirst()
    {
        var svc = CreateService();
        var result = await svc.GetMessagesAsync("chat-1", "current-user");
        Assert.NotEmpty(result.Items);
        var timestamps = result.Items.Select(m => m.Timestamp).ToList();
        for (int i = 0; i < timestamps.Count - 1; i++)
            Assert.True(timestamps[i] >= timestamps[i + 1], "Items should be newest-first");
    }

    [Fact]
    public async Task GetMessagesAsync_NoCursor_HasMoreWhenMoreExist()
    {
        var svc = CreateService();
        var chat = await svc.GetOrCreateChatAsync("user-test-a", "user-test-b");
        for (int i = 0; i < 32; i++)
            await svc.SendMessageAsync(chat.Id, "user-test-a", $"msg {i}");

        var result = await svc.GetMessagesAsync(chat.Id, "user-test-a");
        Assert.Equal(30, result.Items.Count);   // MessagesInitial default
        Assert.True(result.HasMore);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetMessagesAsync_WithCursor_ReturnsOlderBatch()
    {
        var svc = CreateService();
        var chat = await svc.GetOrCreateChatAsync("user-cur-a", "user-cur-b");
        for (int i = 0; i < 35; i++)
            await svc.SendMessageAsync(chat.Id, "user-cur-a", $"msg {i}");

        var page1 = await svc.GetMessagesAsync(chat.Id, "user-cur-a");
        Assert.NotNull(page1.NextCursor);

        var page2 = await svc.GetMessagesAsync(chat.Id, "user-cur-a", page1.NextCursor);
        Assert.NotEmpty(page2.Items);
        var page1OldestTime = page1.Items.Last().Timestamp;
        Assert.All(page2.Items, m => Assert.True(m.Timestamp <= page1OldestTime));
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsEmptyPagedResult_ForNonParticipant()
    {
        var svc = CreateService();
        var result = await svc.GetMessagesAsync("chat-1", "stranger-user");
        Assert.Empty(result.Items);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task SendMessageAsync_PersistsMessage()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "Hello!");
        var history = await svc.GetMessagesAsync("chat-1", "current-user");
        Assert.Contains(history.Items, m => m.Id == msg.Id && m.Content == "Hello!");
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
        Assert.Contains(recipientView.Items, m => m.Id == msg.Id);
    }

    [Fact]
    public async Task SendMessageAsync_CalledByHub_DoesNotExcludeSenderFromPersistence()
    {
        // OthersInGroup only affects SignalR broadcast, not persistence;
        // sender's own GetMessages should still include the sent message
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "Self-visible");
        var senderView = await svc.GetMessagesAsync("chat-1", "current-user");
        Assert.Contains(senderView.Items, m => m.Id == msg.Id);
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
        var persisted = history.Items.First(m => m.Id == msg.Id);
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
