using Lovecraft.Backend.Constants;
using Lovecraft.Backend.MockData;
using Lovecraft.Common.DTOs.Chats;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services;

public class MockChatService : IChatService
{
    public Task<List<ChatDto>> GetChatsAsync(string userId)
    {
        var chats = MockDataStore.Chats
            .Where(c => c.Participants.Contains(userId))
            .Select(c =>
            {
                var lastMsg = MockDataStore.Messages.GetValueOrDefault(c.Id)?.LastOrDefault();
                return new ChatDto
                {
                    Id = c.Id,
                    Type = c.Type,
                    Name = c.Name,
                    Participants = c.Participants,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    EventId = c.EventId,
                    LastMessage = lastMsg,
                    UnreadCount = MockDataStore.UnreadCounts.GetValueOrDefault(userId)?.GetValueOrDefault(c.Id) ?? 0
                };
            })
            .ToList();
        return Task.FromResult(chats);
    }

    public Task<ChatDto?> GetChatAsync(string chatId)
    {
        var chat = MockDataStore.Chats.FirstOrDefault(c => c.Id == chatId);
        return Task.FromResult(chat);
    }

    public Task<ChatDto> GetOrCreateChatAsync(string userId, string targetUserId)
    {
        var existing = MockDataStore.Chats.FirstOrDefault(c =>
            c.Participants.Contains(userId) && c.Participants.Contains(targetUserId));

        if (existing != null)
            return Task.FromResult(existing);

        var chat = new ChatDto
        {
            Id = $"chat-{Guid.NewGuid()}",
            Type = ChatType.Private,
            Participants = new List<string> { userId, targetUserId },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        MockDataStore.Chats.Add(chat);

        if (!MockDataStore.UserChats.ContainsKey(userId))
            MockDataStore.UserChats[userId] = new();
        if (!MockDataStore.UserChats.ContainsKey(targetUserId))
            MockDataStore.UserChats[targetUserId] = new();

        MockDataStore.UserChats[userId].Add((chat.Id, targetUserId, string.Empty, DateTime.UtcNow));
        MockDataStore.UserChats[targetUserId].Add((chat.Id, userId, string.Empty, DateTime.UtcNow));

        if (!MockDataStore.Messages.ContainsKey(chat.Id))
            MockDataStore.Messages[chat.Id] = new();

        return Task.FromResult(chat);
    }

    public Task<List<Lovecraft.Common.DTOs.Chats.MessageDto>> GetMessagesAsync(string chatId, string userId, int page = 1, int pageSize = 50)
    {
        if (!MockDataStore.Chats.Any(c => c.Id == chatId && c.Participants.Contains(userId)))
            return Task.FromResult(new List<Lovecraft.Common.DTOs.Chats.MessageDto>());

        var all = MockDataStore.Messages.GetValueOrDefault(chatId) ?? new();
        // Snippets reference other messages in the same chat — populate them now in case
        // an older message was added since this reply was created.
        var byId = all.ToDictionary(m => m.Id, m => m);
        foreach (var m in all)
        {
            if (!string.IsNullOrEmpty(m.ReplyToMessageId) && byId.TryGetValue(m.ReplyToMessageId, out var target))
                m.ReplyToSnippet = BuildSnippet(target);
        }
        var paged = all
            .OrderByDescending(m => m.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .OrderBy(m => m.Timestamp) // oldest-first to client
            .ToList();
        return Task.FromResult(paged);
    }

    public Task<Lovecraft.Common.DTOs.Chats.MessageDto> SendMessageAsync(string chatId, string userId, string content, List<string>? imageUrls = null, string? replyToMessageId = null)
    {
        var chat = MockDataStore.Chats.FirstOrDefault(c => c.Id == chatId && c.Participants.Contains(userId))
            ?? throw new InvalidOperationException("Chat not found or access denied");

        Lovecraft.Common.DTOs.Chats.MessageReplySnippetDto? snippet = null;
        if (!string.IsNullOrEmpty(replyToMessageId))
        {
            var target = MockDataStore.Messages.GetValueOrDefault(chatId)?.FirstOrDefault(m => m.Id == replyToMessageId)
                ?? throw new ChatMessageException("INVALID_REPLY_TARGET", "Reply target not found in this chat");
            snippet = BuildSnippet(target);
        }

        var msg = new Lovecraft.Common.DTOs.Chats.MessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            SenderId = userId,
            Content = content,
            Timestamp = DateTime.UtcNow,
            Read = false,
            Type = MessageType.Text,
            ImageUrls = imageUrls ?? new List<string>(),
            ReplyToMessageId = string.IsNullOrEmpty(replyToMessageId) ? null : replyToMessageId,
            ReplyToSnippet = snippet,
        };

        if (!MockDataStore.Messages.TryGetValue(chatId, out var msgList))
        {
            msgList = new List<Lovecraft.Common.DTOs.Chats.MessageDto>();
            MockDataStore.Messages[chatId] = msgList;
        }
        msgList.Add(msg);

        // Update UserChats index for both participants; bump unread for everyone but the sender.
        foreach (var participantId in chat.Participants)
        {
            var entries = MockDataStore.UserChats.GetValueOrDefault(participantId);
            if (entries != null)
            {
                var idx = entries.FindIndex(e => e.ChatId == chatId);
                if (idx >= 0)
                    entries[idx] = (chatId, entries[idx].OtherUserId, content, msg.Timestamp);
            }
            if (participantId != userId)
            {
                if (!MockDataStore.UnreadCounts.TryGetValue(participantId, out var counts))
                    MockDataStore.UnreadCounts[participantId] = counts = new();
                counts[chatId] = counts.GetValueOrDefault(chatId) + 1;
            }
        }

        return Task.FromResult(msg);
    }

    public Task<bool> ValidateAccessAsync(string chatId, string userId)
    {
        var result = MockDataStore.Chats.Any(c => c.Id == chatId && c.Participants.Contains(userId));
        return Task.FromResult(result);
    }

    public Task<Lovecraft.Common.DTOs.Chats.MessageDto> SetReactionAsync(string chatId, string messageId, string userId, string emoji)
    {
        if (!AllowedReactions.IsAllowed(emoji))
            throw new ChatReactionException("INVALID_EMOJI", $"Emoji '{emoji}' is not in the allowed set");
        var msg = FindMessage(chatId, messageId)
            ?? throw new ChatReactionException("MESSAGE_NOT_FOUND", "Message not found");
        if (msg.SenderId == userId)
            throw new ChatReactionException("CANT_REACT_TO_OWN", "You cannot react to your own message");
        msg.Reactions[userId] = emoji;
        return Task.FromResult(msg);
    }

    public Task<Lovecraft.Common.DTOs.Chats.MessageDto> RemoveReactionAsync(string chatId, string messageId, string userId)
    {
        var msg = FindMessage(chatId, messageId)
            ?? throw new ChatReactionException("MESSAGE_NOT_FOUND", "Message not found");
        msg.Reactions.Remove(userId);
        return Task.FromResult(msg);
    }

    public Task<Lovecraft.Common.DTOs.Chats.MessageDto> EditMessageAsync(string chatId, string messageId, string userId, string newContent)
    {
        var msg = FindMessage(chatId, messageId)
            ?? throw new ChatMessageException("MESSAGE_NOT_FOUND", "Message not found");
        if (msg.SenderId != userId)
            throw new ChatMessageException("NOT_MESSAGE_OWNER", "You can only edit your own messages");
        if (DateTime.UtcNow - msg.Timestamp > ChatEditPolicy.EditWindow)
            throw new ChatMessageException("EDIT_WINDOW_EXPIRED", "The edit window for this message has passed");

        msg.Content = newContent;
        msg.EditedAt = DateTime.UtcNow;

        // Keep the chat-list preview in sync when the newest message was edited.
        var latest = MockDataStore.Messages.GetValueOrDefault(chatId)?
            .OrderByDescending(m => m.Timestamp).FirstOrDefault();
        if (latest?.Id == messageId)
        {
            var chat = MockDataStore.Chats.FirstOrDefault(c => c.Id == chatId);
            foreach (var participantId in chat?.Participants ?? new List<string>())
            {
                var entries = MockDataStore.UserChats.GetValueOrDefault(participantId);
                if (entries == null) continue;
                var idx = entries.FindIndex(e => e.ChatId == chatId);
                if (idx >= 0)
                    entries[idx] = (chatId, entries[idx].OtherUserId, newContent, entries[idx].LastAt);
            }
        }

        return Task.FromResult(msg);
    }

    public Task MarkChatReadAsync(string chatId, string userId)
    {
        if (MockDataStore.UnreadCounts.TryGetValue(userId, out var counts))
            counts[chatId] = 0;
        return Task.CompletedTask;
    }

    private static Lovecraft.Common.DTOs.Chats.MessageDto? FindMessage(string chatId, string messageId) =>
        MockDataStore.Messages.GetValueOrDefault(chatId)?.FirstOrDefault(m => m.Id == messageId);

    private static Lovecraft.Common.DTOs.Chats.MessageReplySnippetDto BuildSnippet(Lovecraft.Common.DTOs.Chats.MessageDto target)
    {
        var preview = target.Content ?? string.Empty;
        if (preview.Length > 100) preview = preview.Substring(0, 100);
        return new Lovecraft.Common.DTOs.Chats.MessageReplySnippetDto
        {
            Id = target.Id,
            SenderId = target.SenderId,
            ContentPreview = preview,
            HasImages = target.ImageUrls?.Count > 0,
        };
    }
}
