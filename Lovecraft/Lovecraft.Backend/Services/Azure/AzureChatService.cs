using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Lovecraft.Backend.Constants;
using Lovecraft.Backend.Storage;
using Lovecraft.Backend.Storage.Entities;
using Lovecraft.Common.DTOs.Chats;
using Lovecraft.Common.Enums;

namespace Lovecraft.Backend.Services.Azure;

public class AzureChatService : IChatService
{
    private readonly TableClient _chatsTable;
    private readonly TableClient _userChatsTable;
    private readonly TableClient _messagesTable;

    public AzureChatService(TableServiceClient tableService)
    {
        _chatsTable    = tableService.GetTableClient(TableNames.Chats);
        _userChatsTable = tableService.GetTableClient(TableNames.UserChats);
        _messagesTable = tableService.GetTableClient(TableNames.Messages);

        Task.WhenAll(
            _chatsTable.CreateIfNotExistsAsync(),
            _userChatsTable.CreateIfNotExistsAsync(),
            _messagesTable.CreateIfNotExistsAsync()
        ).GetAwaiter().GetResult();
    }

    public async Task<List<ChatDto>> GetChatsAsync(string userId)
    {
        var entries = _userChatsTable
            .QueryAsync<UserChatEntity>(e => e.PartitionKey == userId)
            .AsPages();

        var result = new List<ChatDto>();
        await foreach (var page in entries)
        {
            foreach (var entry in page.Values)
            {
                result.Add(new ChatDto
                {
                    Id = entry.RowKey,
                    Type = ChatType.Private,
                    Participants = new List<string> { userId, entry.OtherUserId },
                    LastMessage = string.IsNullOrEmpty(entry.LastMessageContent) ? null : new MessageDto
                    {
                        ChatId = entry.RowKey,
                        Content = entry.LastMessageContent,
                        Timestamp = entry.LastMessageAt
                    },
                    CreatedAt = entry.UpdatedAt
                });
            }
        }
        return result.OrderByDescending(c => c.LastMessage?.Timestamp ?? c.CreatedAt).ToList();
    }

    public async Task<ChatDto?> GetChatAsync(string chatId)
    {
        try
        {
            var row = await _chatsTable.GetEntityAsync<ChatEntity>("CHAT", chatId);
            var participants = row.Value.ParticipantIds.Split(',').ToList();
            return new ChatDto
            {
                Id = chatId,
                Type = ChatType.Private,
                Participants = participants,
                CreatedAt = row.Value.CreatedAt
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<ChatDto> GetOrCreateChatAsync(string userId, string targetUserId)
    {
        // Check if index entry exists for userId
        var existing = _userChatsTable
            .QueryAsync<UserChatEntity>(e => e.PartitionKey == userId && e.OtherUserId == targetUserId);

        await foreach (var entry in existing)
        {
            return new ChatDto
            {
                Id = entry.RowKey,
                Type = ChatType.Private,
                Participants = new List<string> { userId, targetUserId }
            };
        }

        // Create new chat
        var chatId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var chatEntity = new ChatEntity
        {
            PartitionKey = "CHAT",
            RowKey = chatId,
            ParticipantIds = $"{userId},{targetUserId}",
            CreatedAt = now
        };
        try
        {
            await _chatsTable.AddEntityAsync(chatEntity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Concurrent creation — return whatever chatId was set (idempotent)
        }

        var indexA = new UserChatEntity { PartitionKey = userId,       RowKey = chatId, OtherUserId = targetUserId, UpdatedAt = now, LastMessageAt = now };
        var indexB = new UserChatEntity { PartitionKey = targetUserId, RowKey = chatId, OtherUserId = userId,       UpdatedAt = now, LastMessageAt = now };
        await _userChatsTable.UpsertEntityAsync(indexA);
        await _userChatsTable.UpsertEntityAsync(indexB);

        return new ChatDto
        {
            Id = chatId,
            Type = ChatType.Private,
            Participants = new List<string> { userId, targetUserId },
            CreatedAt = now
        };
    }

    public async Task<List<MessageDto>> GetMessagesAsync(string chatId, string userId, int page = 1, int pageSize = 50)
    {
        if (!await ValidateAccessAsync(chatId, userId))
            return new List<MessageDto>();

        // Fetch all messages for this chat (inverted ticks = newest first in storage)
        // Then skip/take for pagination, re-reverse to oldest-first for client
        var all = new List<MessageEntity>();
        await foreach (var entity in _messagesTable.QueryAsync<MessageEntity>(e => e.PartitionKey == chatId))
            all.Add(entity);

        // Build a lookup so we can attach a reply snippet without re-querying per row.
        var byId = all.ToDictionary(e => e.MessageId, e => e);

        return all
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .OrderBy(e => e.SentAt) // oldest-first to client
            .Select(e => EntityToDto(e, chatId, byId))
            .ToList();
    }

    private static MessageDto EntityToDto(MessageEntity e, string chatId, IReadOnlyDictionary<string, MessageEntity> byId)
    {
        var dto = new MessageDto
        {
            Id = e.MessageId,
            ChatId = chatId,
            SenderId = e.SenderId,
            Content = e.Content,
            Timestamp = e.SentAt,
            EditedAt = e.EditedAt,
            Read = e.Read,
            Type = MessageType.Text,
            ImageUrls = JsonSerializer.Deserialize<List<string>>(e.ImageUrls ?? "[]") ?? new List<string>(),
            Reactions = JsonSerializer.Deserialize<Dictionary<string, string>>(string.IsNullOrEmpty(e.Reactions) ? "{}" : e.Reactions) ?? new Dictionary<string, string>(),
        };
        if (!string.IsNullOrEmpty(e.ReplyToMessageId))
        {
            dto.ReplyToMessageId = e.ReplyToMessageId;
            if (byId.TryGetValue(e.ReplyToMessageId, out var target))
                dto.ReplyToSnippet = BuildSnippet(target);
        }
        return dto;
    }

    private static MessageReplySnippetDto BuildSnippet(MessageEntity target)
    {
        var preview = target.Content ?? string.Empty;
        if (preview.Length > 100) preview = preview.Substring(0, 100);
        var images = JsonSerializer.Deserialize<List<string>>(target.ImageUrls ?? "[]") ?? new List<string>();
        return new MessageReplySnippetDto
        {
            Id = target.MessageId,
            SenderId = target.SenderId,
            ContentPreview = preview,
            HasImages = images.Count > 0,
        };
    }

    public async Task<MessageDto> SendMessageAsync(string chatId, string userId, string content, List<string>? imageUrls = null, string? replyToMessageId = null)
    {
        if (!await ValidateAccessAsync(chatId, userId))
            throw new InvalidOperationException("Access denied");

        // Resolve and validate reply target before we write.
        MessageReplySnippetDto? snippet = null;
        if (!string.IsNullOrEmpty(replyToMessageId))
        {
            MessageEntity? target = null;
            await foreach (var t in _messagesTable.QueryAsync<MessageEntity>(
                e => e.PartitionKey == chatId && e.MessageId == replyToMessageId))
            {
                target = t;
                break;
            }
            if (target is null)
                throw new ChatMessageException("INVALID_REPLY_TARGET", "Reply target not found in this chat");
            snippet = BuildSnippet(target);
        }

        var now = DateTime.UtcNow;
        var msgId = Guid.NewGuid().ToString();
        var invertedTicks = DateTimeOffset.MaxValue.Ticks - now.Ticks;
        var rowKey = $"{invertedTicks:D20}_{msgId}";

        var entity = new MessageEntity
        {
            PartitionKey = chatId,
            RowKey = rowKey,
            MessageId = msgId,
            SenderId = userId,
            Content = content,
            SentAt = now,
            Type = "text",
            Read = false,
            ImageUrls = JsonSerializer.Serialize(imageUrls ?? new List<string>()),
            ReplyToMessageId = replyToMessageId ?? string.Empty,
        };
        await _messagesTable.AddEntityAsync(entity);

        // Update both UserChats index rows
        var chatRow = await _chatsTable.GetEntityAsync<ChatEntity>("CHAT", chatId);
        var participants = chatRow.Value.ParticipantIds.Split(',');
        foreach (var participantId in participants)
        {
            try
            {
                var indexRow = await _userChatsTable.GetEntityAsync<UserChatEntity>(participantId, chatId);
                var updated = indexRow.Value;
                updated.LastMessageContent = content;
                updated.LastMessageAt = now;
                updated.UpdatedAt = now;
                if (participantId != userId) updated.UnreadCount++;
                await _userChatsTable.UpdateEntityAsync(updated, updated.ETag);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Index row missing — skip (shouldn't happen if GetOrCreateChatAsync ran first)
            }
        }

        return new MessageDto
        {
            Id = msgId,
            ChatId = chatId,
            SenderId = userId,
            Content = content,
            Timestamp = now,
            Read = false,
            Type = MessageType.Text,
            ImageUrls = imageUrls ?? new List<string>(),
            ReplyToMessageId = string.IsNullOrEmpty(replyToMessageId) ? null : replyToMessageId,
            ReplyToSnippet = snippet,
        };
    }

    public async Task<bool> ValidateAccessAsync(string chatId, string userId)
    {
        try
        {
            var row = await _chatsTable.GetEntityAsync<ChatEntity>("CHAT", chatId);
            return row.Value.ParticipantIds.Split(',').Contains(userId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<MessageDto> SetReactionAsync(string chatId, string messageId, string userId, string emoji)
    {
        if (!AllowedReactions.IsAllowed(emoji))
            throw new ChatReactionException("INVALID_EMOJI", $"Emoji '{emoji}' is not in the allowed set");
        return await UpdateReactionsAsync(chatId, messageId, dict => dict[userId] = emoji, ownerCheckUserId: userId);
    }

    public async Task<MessageDto> RemoveReactionAsync(string chatId, string messageId, string userId)
    {
        return await UpdateReactionsAsync(chatId, messageId, dict => dict.Remove(userId), ownerCheckUserId: null);
    }

    public async Task<MessageDto> EditMessageAsync(string chatId, string messageId, string userId, string newContent)
    {
        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            MessageEntity? entity = null;
            await foreach (var e in _messagesTable.QueryAsync<MessageEntity>(
                e => e.PartitionKey == chatId && e.MessageId == messageId))
            {
                entity = e;
                break;
            }
            if (entity is null)
                throw new ChatMessageException("MESSAGE_NOT_FOUND", "Message not found");
            if (entity.SenderId != userId)
                throw new ChatMessageException("NOT_MESSAGE_OWNER", "You can only edit your own messages");
            if (DateTime.UtcNow - entity.SentAt > ChatEditPolicy.EditWindow)
                throw new ChatMessageException("EDIT_WINDOW_EXPIRED", "The edit window for this message has passed");

            var now = DateTime.UtcNow;
            entity.Content = newContent;
            entity.EditedAt = now;

            try
            {
                await _messagesTable.UpdateEntityAsync(entity, entity.ETag);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // ETag mismatch — another writer updated the row (e.g. a reaction); re-read and retry.
                continue;
            }

            // Keep the chat-list preview in sync when the most recent message was edited.
            await UpdateLastMessagePreviewIfLatestAsync(chatId, messageId, newContent, now);

            var reactions = JsonSerializer.Deserialize<Dictionary<string, string>>(
                string.IsNullOrEmpty(entity.Reactions) ? "{}" : entity.Reactions) ?? new Dictionary<string, string>();
            return new MessageDto
            {
                Id = entity.MessageId,
                ChatId = chatId,
                SenderId = entity.SenderId,
                Content = entity.Content,
                Timestamp = entity.SentAt,
                EditedAt = entity.EditedAt,
                Read = entity.Read,
                Type = MessageType.Text,
                ImageUrls = JsonSerializer.Deserialize<List<string>>(entity.ImageUrls ?? "[]") ?? new List<string>(),
                Reactions = reactions,
                ReplyToMessageId = string.IsNullOrEmpty(entity.ReplyToMessageId) ? null : entity.ReplyToMessageId,
            };
        }
        throw new ChatMessageException("EDIT_CONFLICT", "Failed to edit message after multiple retries");
    }

    // If the edited message is the newest in the chat, refresh LastMessageContent on both
    // UserChats index rows so the conversation list reflects the new text.
    private async Task UpdateLastMessagePreviewIfLatestAsync(string chatId, string messageId, string newContent, DateTime editedAt)
    {
        DateTime newestSentAt = DateTime.MinValue;
        string newestId = string.Empty;
        await foreach (var e in _messagesTable.QueryAsync<MessageEntity>(e => e.PartitionKey == chatId))
        {
            if (e.SentAt > newestSentAt) { newestSentAt = e.SentAt; newestId = e.MessageId; }
        }
        if (newestId != messageId) return;

        ChatEntity chatRow;
        try { chatRow = (await _chatsTable.GetEntityAsync<ChatEntity>("CHAT", chatId)).Value; }
        catch (RequestFailedException ex) when (ex.Status == 404) { return; }

        foreach (var participantId in chatRow.ParticipantIds.Split(','))
        {
            try
            {
                var indexRow = await _userChatsTable.GetEntityAsync<UserChatEntity>(participantId, chatId);
                var updated = indexRow.Value;
                updated.LastMessageContent = newContent;
                updated.UpdatedAt = editedAt;
                await _userChatsTable.UpdateEntityAsync(updated, updated.ETag);
            }
            catch (RequestFailedException) { /* missing/conflicting index row — best-effort */ }
        }
    }

    // Locate the (single) message entity by id within its chat partition, apply the mutation
    // to its Reactions dict, and write back with ETag. Retries up to 5 times on concurrent
    // updates. The lookup is a partition scan because RowKey is invertedTicks_{id}, not the id.
    private async Task<MessageDto> UpdateReactionsAsync(
        string chatId,
        string messageId,
        Action<Dictionary<string, string>> mutate,
        string? ownerCheckUserId)
    {
        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            MessageEntity? entity = null;
            await foreach (var e in _messagesTable.QueryAsync<MessageEntity>(
                e => e.PartitionKey == chatId && e.MessageId == messageId))
            {
                entity = e;
                break;
            }
            if (entity is null)
                throw new ChatReactionException("MESSAGE_NOT_FOUND", "Message not found");
            if (ownerCheckUserId is not null && entity.SenderId == ownerCheckUserId)
                throw new ChatReactionException("CANT_REACT_TO_OWN", "You cannot react to your own message");

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                string.IsNullOrEmpty(entity.Reactions) ? "{}" : entity.Reactions) ?? new Dictionary<string, string>();
            mutate(dict);
            entity.Reactions = JsonSerializer.Serialize(dict);

            try
            {
                await _messagesTable.UpdateEntityAsync(entity, entity.ETag);
                return new MessageDto
                {
                    Id = entity.MessageId,
                    ChatId = chatId,
                    SenderId = entity.SenderId,
                    Content = entity.Content,
                    Timestamp = entity.SentAt,
                    EditedAt = entity.EditedAt,
                    Read = entity.Read,
                    Type = MessageType.Text,
                    ImageUrls = JsonSerializer.Deserialize<List<string>>(entity.ImageUrls ?? "[]") ?? new List<string>(),
                    Reactions = dict,
                };
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // ETag mismatch — another writer updated the row; re-read and retry.
                continue;
            }
        }
        throw new ChatReactionException("REACTION_CONFLICT", "Failed to update reaction after multiple retries");
    }
}
