using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace Lovecraft.TelegramBot
{
    public class BotMessageHandler : IBotHandler
    {
        private readonly IBotSender _sender;
        private readonly Lovecraft.Common.ILovecraftApiClient _apiClient;

        public BotMessageHandler(IBotSender sender, Lovecraft.Common.ILovecraftApiClient apiClient)
        {
            _sender = sender;
            _apiClient = apiClient;
        }

        public async Task HandleMessageAsync(Message msg, CancellationToken ct)
        {
            if (msg.From is null) return;

            // simplified authorization for tests: accept any non-null
            var member = new DataContracts.Member();

            var text = msg.Text?.Trim() ?? string.Empty;
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

            if (cmd == "/start")
            {
                // Access code validation: expect /start <access_code>
                var providedCode = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                var expectedCode = Environment.GetEnvironmentVariable("ACCESS_CODE") ?? string.Empty;

                if (string.IsNullOrEmpty(providedCode) || string.IsNullOrEmpty(expectedCode) ||
                    !string.Equals(providedCode, expectedCode, StringComparison.Ordinal))
                {
                    // Localized unauthorized message to match other bot messages
                    await _sender.SendMessageAsync(msg.Chat.Id, "Вы не авторизованы для использования системы.", ct);
                    return;
                }

                await _sender.SendMessageAsync(msg.Chat.Id, "Привет! Я бот для доступа к Lovecraft.", ct);
                try
                {
                    var health = await _apiClient.GetHealthAsync();
                    await _sender.SendMessageAsync(msg.Chat.Id, $"Health: ready={health.Ready}, version={health.Version}, uptime={health.Uptime}", ct);
                }
                catch (Exception ex)
                {
                    await _sender.SendMessageAsync(msg.Chat.Id, $"Не удалось получить данные с сервера (WebAPI).\r\n{ex.ToString()}", ct);
                }
            }
            else if (cmd == "/help")
            {
                await _sender.SendMessageAsync(msg.Chat.Id, "/start - начать работу с ботом\n/help - показать это сообщение", ct);
            }
            else
            {
                await _sender.SendMessageAsync(msg.Chat.Id, "Неизвестная команда, /help - список команд", ct);
            }
        }
    }
}
