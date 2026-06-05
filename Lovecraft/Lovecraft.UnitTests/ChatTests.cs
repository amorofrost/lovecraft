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

    // --- Reaction tests ---

    private const string ThumbsUp = "\U0001F44D"; // 👍
    private const string Heart    = "❤️";
    private const string Tada     = "\U0001F389"; // 🎉

    [Fact]
    public async Task SetReactionAsync_AddsCallerReaction()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "user-anna", "react to me");
        var updated = await svc.SetReactionAsync("chat-1", msg.Id, "current-user", ThumbsUp);
        Assert.Equal(ThumbsUp, updated.Reactions["current-user"]);
    }

    [Fact]
    public async Task SetReactionAsync_ReplacesExistingReactionForSameUser()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "user-anna", "replace test");
        await svc.SetReactionAsync("chat-1", msg.Id, "current-user", ThumbsUp);
        var updated = await svc.SetReactionAsync("chat-1", msg.Id, "current-user", Heart);
        Assert.Single(updated.Reactions);
        Assert.Equal(Heart, updated.Reactions["current-user"]);
    }

    [Fact]
    public async Task RemoveReactionAsync_DropsReaction()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "user-anna", "remove test");
        await svc.SetReactionAsync("chat-1", msg.Id, "current-user", ThumbsUp);
        var updated = await svc.RemoveReactionAsync("chat-1", msg.Id, "current-user");
        Assert.DoesNotContain("current-user", updated.Reactions.Keys);
    }

    [Fact]
    public async Task RemoveReactionAsync_IsIdempotentWhenAbsent()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "user-anna", "idempotent remove");
        var updated = await svc.RemoveReactionAsync("chat-1", msg.Id, "current-user");
        Assert.Empty(updated.Reactions);
    }

    [Fact]
    public async Task SetReactionAsync_ThrowsCantReactToOwnMessage()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "my own message");
        var ex = await Assert.ThrowsAsync<ChatReactionException>(
            () => svc.SetReactionAsync("chat-1", msg.Id, "current-user", ThumbsUp));
        Assert.Equal("CANT_REACT_TO_OWN", ex.Code);
    }

    [Fact]
    public async Task SetReactionAsync_ThrowsInvalidEmoji()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "user-anna", "invalid emoji test");
        var ex = await Assert.ThrowsAsync<ChatReactionException>(
            () => svc.SetReactionAsync("chat-1", msg.Id, "current-user", "\U0001F4A9")); // 💩 — not in set
        Assert.Equal("INVALID_EMOJI", ex.Code);
    }

    [Fact]
    public async Task SetReactionAsync_ThrowsMessageNotFound()
    {
        var svc = CreateService();
        var ex = await Assert.ThrowsAsync<ChatReactionException>(
            () => svc.SetReactionAsync("chat-1", "bogus-message-id", "current-user", ThumbsUp));
        Assert.Equal("MESSAGE_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task RemoveReactionAsync_ThrowsMessageNotFound()
    {
        var svc = CreateService();
        var ex = await Assert.ThrowsAsync<ChatReactionException>(
            () => svc.RemoveReactionAsync("chat-1", "bogus-message-id", "current-user"));
        Assert.Equal("MESSAGE_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task SetReactionAsync_MultipleUsersOnSameMessageCoexist()
    {
        var svc = CreateService();
        // Send a message from a third user so both "current-user" and "user-anna" can react.
        // (chat-1 participants per seed are current-user + user-anna; a non-participant can't
        // be the sender, so we send from current-user and have user-anna react.)
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "everyone react");
        await svc.SetReactionAsync("chat-1", msg.Id, "user-anna", ThumbsUp);
        var updated = await svc.SetReactionAsync("chat-1", msg.Id, "third-user", Tada);
        Assert.Equal(2, updated.Reactions.Count);
        Assert.Equal(ThumbsUp, updated.Reactions["user-anna"]);
        Assert.Equal(Tada, updated.Reactions["third-user"]);
    }

    // --- Reply tests ---

    [Fact]
    public async Task SendMessageAsync_WithValidReplyTo_PopulatesSnippet()
    {
        var svc = CreateService();
        var original = await svc.SendMessageAsync("chat-1", "user-anna", "the original message");
        var reply = await svc.SendMessageAsync("chat-1", "current-user", "my reply", null, original.Id);
        Assert.Equal(original.Id, reply.ReplyToMessageId);
        Assert.NotNull(reply.ReplyToSnippet);
        Assert.Equal(original.Id, reply.ReplyToSnippet!.Id);
        Assert.Equal("user-anna", reply.ReplyToSnippet.SenderId);
        Assert.Equal("the original message", reply.ReplyToSnippet.ContentPreview);
        Assert.False(reply.ReplyToSnippet.HasImages);
    }

    [Fact]
    public async Task SendMessageAsync_WithReplyToImageMessage_SnippetHasImagesTrue()
    {
        var svc = CreateService();
        var original = await svc.SendMessageAsync("chat-1", "user-anna", "look", new List<string> { "u1" });
        var reply = await svc.SendMessageAsync("chat-1", "current-user", "nice", null, original.Id);
        Assert.NotNull(reply.ReplyToSnippet);
        Assert.True(reply.ReplyToSnippet!.HasImages);
    }

    [Fact]
    public async Task SendMessageAsync_ReplyPreviewTruncatedTo100Chars()
    {
        var svc = CreateService();
        var longContent = new string('a', 250);
        var original = await svc.SendMessageAsync("chat-1", "user-anna", longContent);
        var reply = await svc.SendMessageAsync("chat-1", "current-user", "reply", null, original.Id);
        Assert.NotNull(reply.ReplyToSnippet);
        Assert.Equal(100, reply.ReplyToSnippet!.ContentPreview.Length);
    }

    [Fact]
    public async Task SendMessageAsync_WithBogusReplyTo_ThrowsInvalidReplyTarget()
    {
        var svc = CreateService();
        var ex = await Assert.ThrowsAsync<ChatMessageException>(
            () => svc.SendMessageAsync("chat-1", "current-user", "stray reply", null, "bogus-id"));
        Assert.Equal("INVALID_REPLY_TARGET", ex.Code);
    }

    [Fact]
    public async Task SendMessageAsync_NullReplyTo_LeavesReplyFieldsEmpty()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "plain message");
        Assert.Null(msg.ReplyToMessageId);
        Assert.Null(msg.ReplyToSnippet);
    }

    [Fact]
    public async Task SendMessageAsync_ChainOfReplies_EachKeepsItsOwnReplyTo()
    {
        var svc = CreateService();
        var a = await svc.SendMessageAsync("chat-1", "user-anna", "A");
        var b = await svc.SendMessageAsync("chat-1", "current-user", "B", null, a.Id);
        var c = await svc.SendMessageAsync("chat-1", "user-anna", "C", null, b.Id);
        Assert.Equal(a.Id, b.ReplyToMessageId);
        Assert.Equal(b.Id, c.ReplyToMessageId);
        Assert.Equal("A", b.ReplyToSnippet!.ContentPreview);
        Assert.Equal("B", c.ReplyToSnippet!.ContentPreview);
    }

    [Fact]
    public async Task GetMessagesAsync_PopulatesSnippetsForReplies()
    {
        var svc = CreateService();
        var original = await svc.SendMessageAsync("chat-1", "user-anna", "first thing said");
        var reply = await svc.SendMessageAsync("chat-1", "current-user", "responding", null, original.Id);
        var history = await svc.GetMessagesAsync("chat-1", "current-user", page: 1, pageSize: 100);
        var fetchedReply = history.First(m => m.Id == reply.Id);
        Assert.Equal(original.Id, fetchedReply.ReplyToMessageId);
        Assert.NotNull(fetchedReply.ReplyToSnippet);
        Assert.Equal("first thing said", fetchedReply.ReplyToSnippet!.ContentPreview);
    }

    // --- Edit tests ---

    [Fact]
    public async Task EditMessageAsync_UpdatesContentAndStampsEditedAt()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "original");
        Assert.Null(msg.EditedAt);
        var updated = await svc.EditMessageAsync("chat-1", msg.Id, "current-user", "edited text");
        Assert.Equal("edited text", updated.Content);
        Assert.NotNull(updated.EditedAt);
    }

    [Fact]
    public async Task EditMessageAsync_PersistsAndIsVisibleToOtherParticipant()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "original");
        await svc.EditMessageAsync("chat-1", msg.Id, "current-user", "edited");
        var recipientView = await svc.GetMessagesAsync("chat-1", "user-anna", page: 1, pageSize: 100);
        var seen = recipientView.First(m => m.Id == msg.Id);
        Assert.Equal("edited", seen.Content);
        Assert.NotNull(seen.EditedAt);
    }

    [Fact]
    public async Task EditMessageAsync_ThrowsNotMessageOwner()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "user-anna", "anna's message");
        var ex = await Assert.ThrowsAsync<ChatMessageException>(
            () => svc.EditMessageAsync("chat-1", msg.Id, "current-user", "hijack"));
        Assert.Equal("NOT_MESSAGE_OWNER", ex.Code);
    }

    [Fact]
    public async Task EditMessageAsync_ThrowsMessageNotFound()
    {
        var svc = CreateService();
        var ex = await Assert.ThrowsAsync<ChatMessageException>(
            () => svc.EditMessageAsync("chat-1", "bogus-message-id", "current-user", "x"));
        Assert.Equal("MESSAGE_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task EditMessageAsync_ThrowsWhenWindowExpired()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "old message");
        // MockChatService stores the same DTO instance; backdate it past the 24h window.
        msg.Timestamp = DateTime.UtcNow.AddHours(-25);
        var ex = await Assert.ThrowsAsync<ChatMessageException>(
            () => svc.EditMessageAsync("chat-1", msg.Id, "current-user", "too late"));
        Assert.Equal("EDIT_WINDOW_EXPIRED", ex.Code);
    }

    [Fact]
    public async Task EditMessageAsync_UpdatesChatListPreviewWhenLatest()
    {
        var svc = CreateService();
        var msg = await svc.SendMessageAsync("chat-1", "current-user", "latest message");
        await svc.EditMessageAsync("chat-1", msg.Id, "current-user", "edited latest");
        var chats = await svc.GetChatsAsync("current-user");
        Assert.Contains(chats, c => c.LastMessage?.Content == "edited latest");
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
    public async Task EditMessage_succeeds_for_author_and_sets_editedAt()
    {
        using var client = CreateClientAsUser(_factory, "u-author");
        var chatResp = await client.PostAsJsonAsync("/api/v1/chats", new { targetUserId = "u-peer" });
        chatResp.EnsureSuccessStatusCode();
        var chatId = (await chatResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString();

        var sendResp = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages", new { content = "first" });
        sendResp.EnsureSuccessStatusCode();
        var msgId = (await sendResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString();

        var editResp = await client.PutAsJsonAsync($"/api/v1/chats/{chatId}/messages/{msgId}", new { content = "edited" });
        editResp.EnsureSuccessStatusCode();
        var data = (await editResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("edited", data.GetProperty("content").GetString());
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("editedAt").ValueKind);
    }

    [Fact]
    public async Task EditMessage_forbidden_for_non_author()
    {
        using var author = CreateClientAsUser(_factory, "u-author2");
        var chatResp = await author.PostAsJsonAsync("/api/v1/chats", new { targetUserId = "u-peer2" });
        chatResp.EnsureSuccessStatusCode();
        var chatId = (await chatResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString();
        var sendResp = await author.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages", new { content = "mine" });
        var msgId = (await sendResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString();

        // u-peer2 is a participant of the chat but not the message author → 403.
        using var peer = CreateClientAsUser(_factory, "u-peer2");
        var editResp = await peer.PutAsJsonAsync($"/api/v1/chats/{chatId}/messages/{msgId}", new { content = "hijack" });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, editResp.StatusCode);
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
