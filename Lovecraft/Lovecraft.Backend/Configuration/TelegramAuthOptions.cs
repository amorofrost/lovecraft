namespace Lovecraft.Backend.Configuration;

public class TelegramAuthOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Bot token from BotFather; used to verify Login Widget signatures.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>Bot username without @; exposed to the web client for the widget.</summary>
    public string BotUsername { get; set; } = string.Empty;
}
