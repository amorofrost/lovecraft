namespace Lovecraft.TelegramBot;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Lovecraft.Common.Interfaces;

public sealed class BotHostedService : BackgroundService
{
    private const string BotVer = "v0.1.4";

    private readonly ITelegramBotClient _bot;
    private readonly ILogger<BotHostedService> _log;
    private readonly ILovecraftApiClient _apiClient;
    private readonly IBotHandler _handler;
    private readonly IBotSender _sender;

    public BotHostedService(ITelegramBotClient bot, ILogger<BotHostedService> log, ILovecraftApiClient apiClient, IBotHandler handler, IBotSender sender)
    {
        _bot = bot;
        _log = log;
        _apiClient = apiClient;
        _handler = handler;
        _sender = sender;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await _bot.GetMe(stoppingToken);
        _log.LogInformation($"Bot @{me.Username} {BotVer} started");

        _bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, new()
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        }, cancellationToken: stoppingToken);
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        var err = ex switch
        {
            ApiRequestException apiEx => $"Telegram API Error:\n[{apiEx.ErrorCode}] {apiEx.Message}",
            _ => ex.ToString()
        };
        _log.LogError(err);
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message!.Type == MessageType.Text)
            {
                await _handler.HandleMessageAsync(update.Message!, ct);
            }
            else if (update.Type == UpdateType.Message && update.Message!.Type == MessageType.Photo)
            {
                await _handler.HandlePhotoAsync(update.Message!, ct);
            }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                var cb = update.CallbackQuery!;
                if (cb.From is null || cb.Message is null || string.IsNullOrWhiteSpace(cb.Data))
                    return;

                await _handler.HandleCallbackAsync(cb, ct);
            }
            else
            {
                _log.LogInformation($"Unsupported update type: {update.Type}");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Update handling failed");
        }
    }

    private async Task HandleCallback(CallbackQuery cb, CancellationToken ct)
    {
        
    }
}