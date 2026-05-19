namespace Lovecraft.Common.DTOs.Notifications;

public class InternalMuteTypeRequestDto
{
    /// <summary>Telegram user id (as string) the bot received in the callback.</summary>
    public string TelegramUserId { get; set; } = string.Empty;
    /// <summary>NotificationType camelCase name, e.g. "messageReceived".</summary>
    public string Type { get; set; } = string.Empty;
}
