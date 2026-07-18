using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Lovecraft.Common.Localization;

namespace Lovecraft.TelegramBot;

/// <summary>
/// Long-polling Telegram bot: handles /start and basic help. Configure the Mini App URL in BotFather (Menu Button / Web App).
/// </summary>
public class TelegramBotWorker : BackgroundService
{
    private readonly ILogger<TelegramBotWorker> _logger;
    private readonly NotificationCallbackHandler? _callbackHandler;

    public TelegramBotWorker(ILogger<TelegramBotWorker> logger, NotificationCallbackHandler? callbackHandler = null)
    {
        _logger = logger;
        _callbackHandler = callbackHandler;
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
        // Callback queries (mute buttons etc.)
        if (update.CallbackQuery is { } cb)
        {
            var fromId = cb.From.Id;
            var handled = _callbackHandler is not null
                ? await _callbackHandler.HandleMuteCallbackAsync(fromId, cb.Data ?? string.Empty, ct)
                : false;
            if (handled)
            {
                var lang = LanguageResolver.FromTelegramCode(cb.From.LanguageCode);
                await bot.AnswerCallbackQuery(cb.Id, TelegramStrings.Get(lang, TelegramStrings.BotMuteAck), cancellationToken: ct);
            }
            return;
        }

        if (update.Message is not { } message)
            return;

        if (message.Text is not { } text)
            return;

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var lang = LanguageResolver.FromTelegramCode(message.From?.LanguageCode);
            await bot.SendMessage(
                message.Chat.Id,
                TelegramStrings.Get(lang, TelegramStrings.BotStart),
                cancellationToken: ct);
            return;
        }

        if (text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            var lang = LanguageResolver.FromTelegramCode(message.From?.LanguageCode);
            await bot.SendMessage(
                message.Chat.Id,
                TelegramStrings.Get(lang, TelegramStrings.BotHelp),
                cancellationToken: ct);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "Telegram polling error");
        return Task.CompletedTask;
    }
}
