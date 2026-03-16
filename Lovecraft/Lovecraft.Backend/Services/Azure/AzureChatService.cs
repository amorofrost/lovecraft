using Azure;
using Azure.Data.Tables;
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

        return all
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .OrderBy(e => e.SentAt) // oldest-first to client
            .Select(e => new MessageDto
            {
                Id = e.MessageId,
                ChatId = chatId,
                SenderId = e.SenderId,
                Content = e.Content,
                Timestamp = e.SentAt,
                Read = e.Read,
                Type = MessageType.Text
            })
            .ToList();
    }

    public async Task<MessageDto> SendMessageAsync(string chatId, string userId, string content)
    {
        if (!await ValidateAccessAsync(chatId, userId))
            throw new InvalidOperationException("Access denied");

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
            Read = false
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
            Type = MessageType.Text
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
}
