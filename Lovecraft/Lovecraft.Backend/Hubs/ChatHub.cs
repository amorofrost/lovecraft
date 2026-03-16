using Lovecraft.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Lovecraft.Backend.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;

    public ChatHub(IChatService chatService)
    {
        _chatService = chatService;
    }

    private string CurrentUserId =>
        Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "current-user";

    public async Task JoinChat(string chatId)
    {
        if (!await _chatService.ValidateAccessAsync(chatId, CurrentUserId))
        {
            throw new HubException("Access denied to chat.");
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-{chatId}");
    }

    public async Task JoinTopic(string topicId)
    {
        // No access check — any authenticated user may receive live reply updates
        await Groups.AddToGroupAsync(Context.ConnectionId, $"topic-{topicId}");
    }

    public async Task LeaveGroup(string groupId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
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
}
