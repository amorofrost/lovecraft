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
                    LastMessage = lastMsg
                };
            })
            .ToList();
        return Task.FromResult(chats);
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
        var paged = all
            .OrderByDescending(m => m.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .OrderBy(m => m.Timestamp) // oldest-first to client
            .ToList();
        return Task.FromResult(paged);
    }

    public Task<Lovecraft.Common.DTOs.Chats.MessageDto> SendMessageAsync(string chatId, string userId, string content)
    {
        var chat = MockDataStore.Chats.FirstOrDefault(c => c.Id == chatId && c.Participants.Contains(userId))
            ?? throw new InvalidOperationException("Chat not found or access denied");

        var msg = new Lovecraft.Common.DTOs.Chats.MessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            SenderId = userId,
            Content = content,
            Timestamp = DateTime.UtcNow,
            Read = false,
            Type = MessageType.Text
        };

        MockDataStore.Messages.GetValueOrDefault(chatId)?.Add(msg);

        // Update UserChats index for both participants
        foreach (var participantId in chat.Participants)
        {
            var entries = MockDataStore.UserChats.GetValueOrDefault(participantId);
            if (entries == null) continue;
            var idx = entries.FindIndex(e => e.ChatId == chatId);
            if (idx >= 0)
                entries[idx] = (chatId, entries[idx].OtherUserId, content, msg.Timestamp);
        }

        return Task.FromResult(msg);
    }

    public Task<bool> ValidateAccessAsync(string chatId, string userId)
    {
        var result = MockDataStore.Chats.Any(c => c.Id == chatId && c.Participants.Contains(userId));
        return Task.FromResult(result);
    }
}
