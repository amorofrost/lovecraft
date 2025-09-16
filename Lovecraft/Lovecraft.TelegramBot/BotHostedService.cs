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
    private const string BotVer = "v0.0.1";

    private readonly ITelegramBotClient _bot;
    private readonly ILogger<BotHostedService> _log;

    public BotHostedService(ITelegramBotClient bot, ILogger<BotHostedService> log)
    {
        _bot = bot;
        _log = log;
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
                await HandleMessage(update.Message!, ct);
            }
            else if (update.Type == UpdateType.Message && update.Message!.Type == MessageType.Photo)
            {
                // handle photo message
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

    private bool IsAuthorized(User from) => false;
    private bool IsAuthorized(string from) => false;

    private async Task HandleMessage(Message msg, CancellationToken ct)
    {
        _log.LogInformation($"Received message from {msg.From?.Username}|{msg.From?.Id}: {msg.Text} (#{msg.Chat.Id})");

        if (msg.From is null) return;

        if (!IsAuthorized(msg.From))
        {
            _log.LogWarning("Unauthorized access attempt by {Username}", msg.From.Username);
            await _bot.SendMessage(msg.Chat.Id, $"Твой аккаунт ({msg.From.Username}) еще не зарегистрирован", cancellationToken: ct);
            return;
        }

        var member = await HandleInit(msg, ct);
        if (member is null)
        {
            _log.LogWarning("Member initialization failed for {Username}", msg.From.Username);
            await _bot.SendMessage(msg.Chat.Id, "Ошибка инициализации пользователя", cancellationToken: ct);
            return;
        }

        var text = msg.Text!.Trim();
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (cmd)
        {
            case "/start":
                await HandleStartCmd(member, msg, ct);
                break;

            case "/help":
                await HandleHelpCmd(member, msg, ct);
                break;

            default:
                await _bot.SendMessage(msg.Chat.Id, "Неизвестная команда, /help - список команд", cancellationToken: ct);
                break;
        }
    }

    private async Task<Member> HandleInit(Message msg, CancellationToken ct)
    {
        return new Member();
    }

    private async Task HandleStartCmd(Member member, Message msg, CancellationToken ct)
    {
        await _bot.SendMessage(msg.Chat.Id, $"Привет! Я бот для доступа к Lovecraft.", cancellationToken: ct);
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