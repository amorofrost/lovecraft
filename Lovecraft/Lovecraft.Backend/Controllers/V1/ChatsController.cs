using Lovecraft.Backend.Helpers;
using Lovecraft.Backend.Hubs;
using Lovecraft.Backend.Services;
using Lovecraft.Backend.Services.Notifications;
using Lovecraft.Common.DTOs.Chats;
using Lovecraft.Common.Enums;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.Text.Json;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ChatsController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly INotificationProducer _producer;

    public ChatsController(IChatService chatService, IHubContext<ChatHub> hubContext, INotificationProducer producer)
    {
        _chatService = chatService;
        _hubContext = hubContext;
        _producer = producer;
    }

    private string CurrentUserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "current-user";

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ChatDto>>>> GetChats()
    {
        var chats = await _chatService.GetChatsAsync(CurrentUserId);
        return Ok(ApiResponse<List<ChatDto>>.SuccessResponse(chats));
    }

    [HttpGet("{id}/messages")]
    public async Task<ActionResult<ApiResponse<List<MessageDto>>>> GetMessages(
        string id, [FromQuery] int page = 1)
    {
        if (!await _chatService.ValidateAccessAsync(id, CurrentUserId))
            return Forbid();

        var messages = await _chatService.GetMessagesAsync(id, CurrentUserId, page);
        return Ok(ApiResponse<List<MessageDto>>.SuccessResponse(messages));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ChatDto>>> GetOrCreateChat(
        [FromBody] CreatePrivateChatRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetUserId))
            return BadRequest(ApiResponse<ChatDto>.ErrorResponse("TARGET_REQUIRED", "targetUserId is required"));

        var chat = await _chatService.GetOrCreateChatAsync(CurrentUserId, request.TargetUserId);
        return Ok(ApiResponse<ChatDto>.SuccessResponse(chat));
    }

    [HttpPost("{id}/messages")]
    public async Task<ActionResult<ApiResponse<MessageDto>>> SendMessage(
        string id, [FromBody] SendMessageRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(ApiResponse<MessageDto>.ErrorResponse("CONTENT_REQUIRED", "Message content cannot be empty"));

        if (HtmlGuard.ContainsHtml(request.Content))
            return BadRequest(ApiResponse<MessageDto>.ErrorResponse("HTML_NOT_ALLOWED", "HTML tags are not permitted in messages"));

        if (!await _chatService.ValidateAccessAsync(id, CurrentUserId))
            return Forbid();

        var message = await _chatService.SendMessageAsync(id, CurrentUserId, request.Content, request.ImageUrls);

        // Push to all connected members of this chat group in real time.
        // The sender receives it too; the frontend deduplicates by message ID.
        await _hubContext.Clients.Group($"chat-{id}").SendAsync("MessageReceived", message);

        // Fire in-app notifications for each non-sender participant.
        // The producer's DerivePresenceGroup extracts "chat-{id}" for in-chat suppression.
        var chat = await _chatService.GetChatAsync(id);
        if (chat is not null)
        {
            var senderId = CurrentUserId;
            var preview = request.Content.Length > 80
                ? request.Content.Substring(0, 80) + "…"
                : request.Content;
            var payloadJson = JsonSerializer.Serialize(new
            {
                chatId = id,
                messageId = message.Id,
                preview,
            });

            foreach (var participantId in chat.Participants)
            {
                if (participantId == senderId) continue;
                await _producer.ProduceAsync(
                    recipientUserId: participantId,
                    type: NotificationType.MessageReceived,
                    actorId: senderId,
                    payloadJson: payloadJson,
                    sourceEventId: message.Id);
            }
        }

        return Ok(ApiResponse<MessageDto>.SuccessResponse(message));
    }
}
