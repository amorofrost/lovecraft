namespace Lovecraft.TelegramBot;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Lovecraft.TelegramBot.DataContracts;

public sealed class BotHostedService : BackgroundService
{
    private const string BotVer = "v0.0.3";

    private readonly ITelegramBotClient _bot;
    private readonly ILogger<BotHostedService> _log;
    private readonly Lovecraft.Common.ILovecraftApiClient _apiClient;
    private readonly IBotHandler _handler;
    private readonly IBotSender _sender;

    public BotHostedService(ITelegramBotClient bot, ILogger<BotHostedService> log, Lovecraft.Common.ILovecraftApiClient apiClient, IBotHandler handler, IBotSender sender)
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
                if (update.Type == UpdateType.Message &&
                    (update.Message!.Type == MessageType.Text || update.Message!.Type == MessageType.Photo))
                {
                    await _handler.HandleMessageAsync(update.Message!, ct);
                }
            else if (update.Type == UpdateType.CallbackQuery)
            {
                await HandleCallback(update.CallbackQuery!, ct);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Update handling failed");
        }
    }

    private bool IsAuthorized(User from) => from.Id == 99108740;
    private bool IsAuthorized(string from) => from == "amorofrost" || from == "99108740";

    // Message handling moved to BotMessageHandler for easier unit testing.

    private Task<Member> HandleInit(Message msg, CancellationToken ct)
    {
        return Task.FromResult(new Member());
    }

        private async Task HandleStartCmd(Member member, Message msg, CancellationToken ct)
    {
        // Greet the user
        await _bot.SendMessage(msg.Chat.Id, $"Привет! Я бот для доступа к Lovecraft.", cancellationToken: ct);

        // Try to call the protected WebAPI using the typed ApiClient (mTLS configured)
    try
    {
        var health = await _apiClient.GetHealthAsync();
        await _bot.SendMessage(msg.Chat.Id, $"Health: ready={health.Ready}, version={health.Version}, uptime={health.Uptime}", cancellationToken: ct);
    }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to call WebAPI");
            await _bot.SendMessage(msg.Chat.Id, "Не удалось получить данные с сервера (WebAPI).", cancellationToken: ct);
        }
    }

    private async Task HandleHelpCmd(Member member, Message msg, CancellationToken ct)
    {
        var helpText = "/start - начать работу с ботом\n" +
                       "/help - показать это сообщение";
        await _bot.SendMessage(msg.Chat.Id, helpText, cancellationToken: ct);
    }

    private async Task HandleCallback(CallbackQuery cb, CancellationToken ct)
    {
        if (cb.From is null || string.IsNullOrWhiteSpace(cb.From.Username) || cb.Message is null || string.IsNullOrWhiteSpace(cb.Data))
            return;

        if (!IsAuthorized(cb.From.Username.ToLowerInvariant()))
        {
            await _bot.AnswerCallbackQuery(cb.Id, "Not authorized", cancellationToken: ct);
            return;
        }

        if (cb.Data.StartsWith("like:"))
        {
            // TODO: handle like callback   
        }
        else if (cb.Data.StartsWith("foobar:"))
        {
        }
    }
}