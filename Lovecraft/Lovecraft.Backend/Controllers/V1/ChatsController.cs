using Lovecraft.Backend.Services;
using Lovecraft.Common.DTOs.Chats;
using Lovecraft.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Lovecraft.Backend.Controllers.V1;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ChatsController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatsController(IChatService chatService)
    {
        _chatService = chatService;
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

        if (!await _chatService.ValidateAccessAsync(id, CurrentUserId))
            return Forbid();

        var message = await _chatService.SendMessageAsync(id, CurrentUserId, request.Content);
        return Ok(ApiResponse<MessageDto>.SuccessResponse(message));
    }
}
