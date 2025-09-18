using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using System.Collections.Concurrent;
using Lovecraft.Common.DataContracts;

namespace Lovecraft.TelegramBot
{
    public class BotMessageHandler : IBotHandler
    {
        private readonly IBotSender _sender;
        private readonly Lovecraft.Common.ILovecraftApiClient _apiClient;
        // In-memory registration state per Telegram user id
        private readonly ConcurrentDictionary<long, RegistrationState> _registrations = new();

        public BotMessageHandler(IBotSender sender, Lovecraft.Common.ILovecraftApiClient apiClient)
        {
            _sender = sender;
            _apiClient = apiClient;
        }

        public async Task HandleMessageAsync(Message msg, CancellationToken ct)
        {
            if (msg.From is null) return;

            // simplified authorization for tests: accept any non-null
            var text = msg.Text?.Trim() ?? string.Empty;
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

            // Handle text messages (commands and registration name entry)
            if (!string.IsNullOrWhiteSpace(msg.Text))
            {
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

                    // Authorized: check whether a user already exists for this Telegram id
                    try
                    {
                        var existing = await _apiClient.GetUserByTelegramUserIdAsync(msg.From.Id);
                        if (existing != null)
                        {
                            // known user: greet and show health
                            await _sender.SendMessageAsync(msg.Chat.Id, "Привет! Я бот для доступа к Lovecraft.", ct);
                            var health = await _apiClient.GetHealthAsync();
                            await _sender.SendMessageAsync(msg.Chat.Id, $"Health: ready={health.Ready}, version={health.Version}, uptime={health.Uptime}", ct);
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        // If health check fails later we'll report; but proceed to offer registration
                        // log is not available here; send minimal info
                    }

                    // User not found -> start registration
                    _registrations[msg.From.Id] = new RegistrationState { Stage = RegistrationStage.WaitingName };
                    await _sender.SendMessageAsync(msg.Chat.Id, "Пользователь не найден. Давайте создадим аккаунт. Пожалуйста, введите ваше имя:", ct);
                    return;
                }
                else if (cmd == "/help")
                {
                    await _sender.SendMessageAsync(msg.Chat.Id, "/start - начать работу с ботом\n/help - показать это сообщение", ct);
                    return;
                }
                else if (cmd == "/me")
                {
                    // Show user's profile if exists
                    try
                    {
                        var existing = await _apiClient.GetUserByTelegramUserIdAsync(msg.From.Id);
                        if (existing == null)
                        {
                            await _sender.SendMessageAsync(msg.Chat.Id, "Аккаунт не найден. Пожалуйста, зарегистрируйтесь сначала с помощью /start <access_code>", ct);
                            return;
                        }

                        // If we have a Telegram avatar file id, send it as a photo
                        if (!string.IsNullOrWhiteSpace(existing.TelegramAvatarFileId))
                        {
                            await _sender.SendPhotoAsync(msg.Chat.Id, existing.TelegramAvatarFileId!, caption: existing.Name, cancellationToken: ct);
                        }
                        else
                        {
                            // Fallback: send name as text
                            await _sender.SendMessageAsync(msg.Chat.Id, $"Имя: {existing.Name}", ct);
                        }
                        return;
                    }
                    catch (System.Exception)
                    {
                        await _sender.SendMessageAsync(msg.Chat.Id, "Ошибка при получении профиля. Попробуйте позже.", ct);
                        return;
                    }
                }

                // If we're in registration waiting for a name, accept the message as name
                if (_registrations.TryGetValue(msg.From.Id, out var state) && state.Stage == RegistrationStage.WaitingName)
                {
                    var name = text;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        await _sender.SendMessageAsync(msg.Chat.Id, "Имя не может быть пустым. Пожалуйста, введите ваше имя:", ct);
                        return;
                    }
                    if (name.Length > Lovecraft.Common.DataContracts.User.MaxNameLength)
                    {
                        await _sender.SendMessageAsync(msg.Chat.Id, $"Имя слишком длинное. Максимальная длина {Lovecraft.Common.DataContracts.User.MaxNameLength} символов. Пожалуйста, введите имя снова:", ct);
                        return;
                    }

                    state.Name = name;
                    state.Stage = RegistrationStage.WaitingPhoto;
                    _registrations[msg.From.Id] = state;
                    await _sender.SendMessageAsync(msg.Chat.Id, "Спасибо. Теперь отправьте фотографию, которая будет использоваться как аватар (фото в чате).", ct);
                    return;
                }

                await _sender.SendMessageAsync(msg.Chat.Id, "Неизвестная команда, /help - список команд", ct);
                return;
            }

            // Handle photo messages as part of registration
            if (msg.Photo != null && msg.Photo.Length > 0)
            {
                if (!_registrations.TryGetValue(msg.From.Id, out var reg) || reg.Stage != RegistrationStage.WaitingPhoto)
                {
                    // Not in registration -> ignore or inform
                    await _sender.SendMessageAsync(msg.Chat.Id, "Я не ожидаю фото от вас. Используйте /start для начала.", ct);
                    return;
                }

                // select largest photo (last in array is largest typically)
                var photos = msg.Photo;
                if (photos == null || photos.Length == 0)
                {
                    await _sender.SendMessageAsync(msg.Chat.Id, "Не удалось прочитать отправленное фото. Попробуйте ещё раз.", ct);
                    return;
                }
                var photo = photos[photos.Length - 1];
                var fileId = photo.FileId;
                reg.TelegramAvatarFileId = fileId;
                reg.Stage = RegistrationStage.Completed;
                _registrations[msg.From.Id] = reg;

                // Build CreateUserRequest - use the telegram file id as AvatarUri (API requires AvatarUri non-empty)
                var createReq = new CreateUserRequest
                {
                    Name = reg.Name ?? string.Empty,
                    AvatarUri = fileId,
                    TelegramUserId = msg.From.Id,
                    TelegramUsername = msg.From.Username,
                    TelegramAvatarFileId = fileId
                };

                try
                {
                    var created = await _apiClient.CreateUserAsync(createReq);
                    await _sender.SendMessageAsync(msg.Chat.Id, $"Аккаунт создан. Ваш id: {created.Id}\nИмя: {created.Name}", ct);
                    // Optionally fetch health after successful registration
                    try
                    {
                        var health = await _apiClient.GetHealthAsync();
                        await _sender.SendMessageAsync(msg.Chat.Id, $"Health: ready={health.Ready}, version={health.Version}, uptime={health.Uptime}", ct);
                    }
                    catch { }

                    // registration finished - remove from in-memory store
                    _registrations.TryRemove(msg.From.Id, out _);
                    return;
                }
                catch (Exception)
                {
                    // Handle conflicts and other errors
                    await _sender.SendMessageAsync(msg.Chat.Id, "Не удалось создать аккаунт из-за ошибки на сервере. Пожалуйста, попробуйте позже.", ct);
                    // keep registration state so user can retry
                    reg.Stage = RegistrationStage.WaitingPhoto;
                    _registrations[msg.From.Id] = reg;
                    return;
                }
            }

            // Unhandled message types
            await _sender.SendMessageAsync(msg.Chat.Id, "Неизвестный тип сообщения. Используйте /help для справки.", ct);
        }

        private class RegistrationState
        {
            public RegistrationStage Stage { get; set; }
            public string? Name { get; set; }
            public string? TelegramAvatarFileId { get; set; }
        }

        private enum RegistrationStage
        {
            WaitingName,
            WaitingPhoto,
            Completed
        }
    }
}
