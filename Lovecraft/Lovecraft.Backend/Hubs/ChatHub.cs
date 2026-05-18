using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace Lovecraft.Backend.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly IPresenceTracker _presence;

    // Tracks which groups each connection has joined so we can clean up on disconnect.
    private static readonly ConcurrentDictionary<string, HashSet<string>> ConnectionGroups = new();

    public ChatHub(IChatService chatService, IPresenceTracker presence)
    {
        _chatService = chatService;
        _presence = presence;
    }

    private string CurrentUserId =>
        Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "current-user";

    public async Task JoinChat(string chatId)
    {
        if (!await _chatService.ValidateAccessAsync(chatId, CurrentUserId))
        {
            throw new HubException("Access denied to chat.");
        }
        var groupName = $"chat-{chatId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _presence.Join(groupName, CurrentUserId);
        RecordConnectionGroup(Context.ConnectionId, groupName);
    }

    public async Task JoinTopic(string topicId)
    {
        // No access check — any authenticated user may receive live reply updates
        var groupName = $"topic-{topicId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _presence.Join(groupName, CurrentUserId);
        RecordConnectionGroup(Context.ConnectionId, groupName);
    }

    public async Task LeaveGroup(string groupId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
        _presence.Leave(groupId, CurrentUserId);
        UnrecordConnectionGroup(Context.ConnectionId, groupId);
    }

    public async Task SendMessage(string chatId, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new HubException("Message content cannot be empty.");

        if (!await _chatService.ValidateAccessAsync(chatId, CurrentUserId))
            throw new HubException("Access denied to chat.");

        var message = await _chatService.SendMessageAsync(chatId, CurrentUserId, content);

        // Broadcast to all group members EXCEPT the sender (sender gets it from REST response)
        await Clients.OthersInGroup($"chat-{chatId}").SendAsync("MessageReceived", message);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = CurrentUserId;
        if (ConnectionGroups.TryRemove(Context.ConnectionId, out var groups))
        {
            foreach (var group in groups)
                _presence.Leave(group, userId);
        }
        return base.OnDisconnectedAsync(exception);
    }

    // ---- helpers ----

    private static void RecordConnectionGroup(string connectionId, string groupName)
    {
        ConnectionGroups.AddOrUpdate(
            connectionId,
            _ => new HashSet<string> { groupName },
            (_, existing) => { lock (existing) { existing.Add(groupName); } return existing; });
    }

    private static void UnrecordConnectionGroup(string connectionId, string groupName)
    {
        if (ConnectionGroups.TryGetValue(connectionId, out var groups))
            lock (groups) { groups.Remove(groupName); }
    }
}
