using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Lovecraft.TelegramBot;

/// <summary>
/// Long-polling Telegram bot: handles /start and basic help. Configure the Mini App URL in BotFather (Menu Button / Web App).
/// </summary>
public class TelegramBotWorker : BackgroundService
{
    private readonly ILogger<TelegramBotWorker> _logger;

    public TelegramBotWorker(ILogger<TelegramBotWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("TELEGRAM_BOT_TOKEN is not set; Telegram bot worker exiting.");
            return;
        }

        var bot = new TelegramBotClient(token);
        var me = await bot.GetMe(stoppingToken);
        _logger.LogInformation("Telegram bot @{Username} ({Id}) polling started", me.Username, me.Id);

        var handler = new DefaultUpdateHandler(HandleUpdateAsync, HandlePollingErrorAsync);
        await bot.ReceiveAsync(handler, cancellationToken: stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message)
            return;

        if (message.Text is not { } text)
            return;

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await bot.SendMessage(
                message.Chat.Id,
                "AloeVera Harmony Meet — use the menu button to open the mini app, or sign in on the website with Telegram.",
                cancellationToken: ct);
            return;
        }

        if (text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            await bot.SendMessage(
                message.Chat.Id,
                "Commands: /start — welcome. Open the Mini App from the bot menu for the web experience inside Telegram.",
                cancellationToken: ct);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Telegram polling error");
        return Task.CompletedTask;
    }
}
