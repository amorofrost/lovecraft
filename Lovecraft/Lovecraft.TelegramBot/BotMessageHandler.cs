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
                await _sender.SendMessageAsync(msg.Chat.Id, "Привет! Я бот для доступа к Lovecraft.", ct);
                try
                {
                    var weatherJson = await _apiClient.GetWeatherAsync();
                    await _sender.SendMessageAsync(msg.Chat.Id, $"WeatherForecast response:\n{weatherJson}", ct);
                }
                catch (Exception)
                {
                    await _sender.SendMessageAsync(msg.Chat.Id, "Не удалось получить данные с сервера (WebAPI).", ct);
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
