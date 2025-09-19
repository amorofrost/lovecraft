using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Lovecraft.Common.DataContracts;
using Lovecraft.Common.Interfaces;
using Lovecraft.Common.Services;


namespace Lovecraft.TelegramBot
{
    public class BotMessageHandler : IBotHandler
    {
        private readonly IBotSender _sender;
        private readonly ILovecraftApiClient _apiClient;

        private IAccessCodeManager _accessCodeManager;

        private readonly ILogger<BotMessageHandler> _logger;

    // In-memory registration state per Telegram user id
    private readonly ConcurrentDictionary<long, RegistrationState> _registrations = new();

    private readonly Dictionary<string, Func<Common.DataContracts.User?, Message, string, CancellationToken, Task>> CommandHandlers;

        public BotMessageHandler(IBotSender sender, ILovecraftApiClient apiClient, IAccessCodeManager accessCodeManager, ILogger<BotMessageHandler> logger)
        {
            _sender = sender;
            _apiClient = apiClient;
            _accessCodeManager = accessCodeManager;
            _logger = logger;

            CommandHandlers = new Dictionary<string, Func<Common.DataContracts.User?, Message, string, CancellationToken, Task>>(StringComparer.OrdinalIgnoreCase)
            {
                { "/start", HandleStartCmd },
                { "/help", HandleHelpCmd },
                { "/me", HandleMeCmd },
                { "/next", HandleNextCmd }
            };
        }

        public async Task HandleMessageAsync(Message msg, CancellationToken ct)
        {
            if (msg.From is null) return;

            var text = msg.Text?.Trim() ?? string.Empty;
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            string arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            // TODO: check in cache first
            var member = await _apiClient.GetUserByTelegramUserIdAsync(msg.From.Id);
            if (member == default && !text.StartsWith("/"))
            {
                // handle registration flow
                await HandleRegistrationAsync(msg, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(cmd))
            {
                await _sender.SendMessageAsync(msg.Chat.Id, "Неизвестная команда. Используйте /help для справки.", ct);
                return;
            }

            if (CommandHandlers.TryGetValue(cmd, out var handler))
            {
                await handler(member, msg, arg, ct);
                return;
            }
            else
            {
                // TODO: check for interactive states (e.g. registration)
                await _sender.SendMessageAsync(msg.Chat.Id, "Неизвестная команда. Используйте /help для справки.", ct);
                return;
            }
        }

        public async Task HandlePhotoAsync(Message msg, CancellationToken ct)
        { 
            if (msg.From is null)
                return;

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
                    TelegramAvatarFileId = fileId,
                    Username = reg.Username,
                    Password = reg.Password
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
                catch (System.Net.Http.HttpRequestException httpEx)
                {
                    // Http client errors from API (e.g., 409 Conflict) are surfaced as HttpRequestException.
                    _logger.LogWarning(httpEx, "CreateUserAsync returned HTTP error during registration");

                    // If the response was a 409 Conflict, likely a duplicate (username/telegram) detected by server.
                    // Prompt the user to choose another login username and move them back to that step.
                    var isConflict = false;
                    try
                    {
                        // HttpRequestException may have a StatusCode property on newer runtimes; check it first.
                        var statusProp = httpEx.GetType().GetProperty("StatusCode");
                        if (statusProp != null)
                        {
                            var val = statusProp.GetValue(httpEx);
                            if (val != null)
                            {
                                var s = val.ToString() ?? string.Empty;
                                if (s.Contains("409"))
                                    isConflict = true;
                            }
                        }
                        else if (httpEx.Message != null && httpEx.Message.Contains("409"))
                        {
                            isConflict = true;
                        }
                    }
                    catch { }

                    if (isConflict)
                    {
                        // Ask user to pick another username. Reset to WaitingUsername stage so they can re-enter it.
                        reg.Stage = RegistrationStage.WaitingUsername;
                        // Remove any stored login credentials so they re-enter
                        reg.Username = null;
                        reg.Password = null;
                        _registrations[msg.From.Id] = reg;
                        await _sender.SendMessageAsync(msg.Chat.Id, "Имя пользователя или Telegram имя уже занято. Пожалуйста, введите другое имя пользователя:", ct);
                        return;
                    }

                    // Generic fallback for other HTTP errors
                    _logger.LogError(httpEx, "Failed to create user during registration");
                    await _sender.SendMessageAsync(msg.Chat.Id, "Не удалось создать аккаунт из-за ошибки на сервере. Пожалуйста, попробуйте позже.", ct);
                    // keep registration state so user can retry uploading photo later
                    reg.Stage = RegistrationStage.WaitingPhoto;
                    _registrations[msg.From.Id] = reg;
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create user during registration");
                    await _sender.SendMessageAsync(msg.Chat.Id, "Не удалось создать аккаунт из-за ошибки на сервере. Пожалуйста, попробуйте позже.", ct);
                    reg.Stage = RegistrationStage.WaitingPhoto;
                    _registrations[msg.From.Id] = reg;
                    return;
                }
            }
        }

        public async Task HandleCallbackAsync(CallbackQuery cb, CancellationToken ct)
        {
            if (cb.From is null || string.IsNullOrWhiteSpace(cb.From.Username) || cb.Message is null || string.IsNullOrWhiteSpace(cb.Data))
                return;

            if (cb.Data.StartsWith("like:"))
            {
                await _sender.AnswerCallbackQueryAsync(cb.Id, "Liked! 👍 (?)", showAlert: false, cancellationToken: ct);
            }
        }

        private async Task HandleRegistrationAsync(Message msg, CancellationToken ct)
        {
            if (msg.From is null)
                return;
            if (_registrations.TryGetValue(msg.From.Id, out var state))
            {
                var text = msg.Text?.Trim() ?? string.Empty;

                // Waiting for user's name
                if (state.Stage == RegistrationStage.WaitingName)
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
                    state.Stage = RegistrationStage.WaitingUsername;
                    _registrations[msg.From.Id] = state;
                    await _sender.SendMessageAsync(msg.Chat.Id, "Спасибо. Пожалуйста, введите желаемое имя пользователя (login):", ct);
                    return;
                }

                // Waiting for username
                if (state.Stage == RegistrationStage.WaitingUsername)
                {
                    var username = text;
                    if (string.IsNullOrWhiteSpace(username))
                    {
                        await _sender.SendMessageAsync(msg.Chat.Id, "Имя пользователя не может быть пустым. Пожалуйста, введите желаемое имя пользователя:", ct);
                        return;
                    }
                    if (username.Length > Lovecraft.Common.DataContracts.User.MaxUsernameLength)
                    {
                        await _sender.SendMessageAsync(msg.Chat.Id, $"Имя пользователя слишком длинное. Максимальная длина {Lovecraft.Common.DataContracts.User.MaxUsernameLength} символов. Пожалуйста, введите имя пользователя снова:", ct);
                        return;
                    }

                    var normalized = username.Trim().ToLowerInvariant();
                    try
                    {
                        var available = await _apiClient.IsUsernameAvailableAsync(normalized);
                        if (!available)
                        {
                            await _sender.SendMessageAsync(msg.Chat.Id, "Это имя пользователя уже занято. Пожалуйста, введите другое имя пользователя:", ct);
                            // keep stage at WaitingUsername and do not overwrite stored username
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        // On API errors, log and ask user to try again later
                        _logger.LogWarning(ex, "Failed to check username availability");
                        await _sender.SendMessageAsync(msg.Chat.Id, "Не удалось проверить доступность имени пользователя. Попробуйте ещё раз позже.", ct);
                        return;
                    }

                    state.Username = normalized;
                    state.Stage = RegistrationStage.WaitingPassword;
                    _registrations[msg.From.Id] = state;
                    await _sender.SendMessageAsync(msg.Chat.Id, "Отлично. Теперь введите пароль (он будет храниться в хэшированном виде):", ct);
                    return;
                }

                // Waiting for password
                if (state.Stage == RegistrationStage.WaitingPassword)
                {
                    var password = text;
                    if (string.IsNullOrWhiteSpace(password))
                    {
                        await _sender.SendMessageAsync(msg.Chat.Id, "Пароль не может быть пустым. Пожалуйста, введите пароль:", ct);
                        return;
                    }
                    // Basic length check; server will re-check stricter limits
                    if (password.Length > Lovecraft.Common.DataContracts.User.MaxPasswordHashLength)
                    {
                        await _sender.SendMessageAsync(msg.Chat.Id, $"Пароль слишком длинный. Максимальная длина {Lovecraft.Common.DataContracts.User.MaxPasswordHashLength} символов. Пожалуйста, введите пароль снова:", ct);
                        return;
                    }

                    state.Password = password;
                    state.Stage = RegistrationStage.WaitingPhoto;
                    _registrations[msg.From.Id] = state;
                    await _sender.SendMessageAsync(msg.Chat.Id, "Спасибо. Теперь отправьте фотографию, которая будет использоваться как аватар (фото в чате).", ct);
                    return;
                }
            }

            await _sender.SendMessageAsync(msg.Chat.Id, "Неизвестная команда, /help - список команд", ct);
            return;
        }

    private async Task HandleStartCmd(Common.DataContracts.User? member, Message msg, string arg, CancellationToken ct)
        {
            // Access code validation: expect /start <access_code>
            var providedCode = arg;
            
            if (string.IsNullOrEmpty(providedCode) || !_accessCodeManager.IsValidCode(providedCode))
            {
                // Localized unauthorized message to match other bot messages
                await _sender.SendMessageAsync(msg.Chat.Id, "Вы не авторизованы для использования системы.", ct);
                return;
            }

            // Authorized: check whether a user already exists for this Telegram id
            try
            {
                var existing = member;
                if (existing != null)
                {
                    // known user: greet and show health
                    await _sender.SendMessageAsync(msg.Chat.Id, "Привет! Я бот для доступа к Lovecraft.", ct);
                    var health = await _apiClient.GetHealthAsync();
                    await _sender.SendMessageAsync(msg.Chat.Id, $"Health: ready={health.Ready}, version={health.Version}, uptime={health.Uptime}", ct);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existing user during /start");
            }

            // User not found -> start registration
            if (msg.From is null)
            {
                await _sender.SendMessageAsync(msg.Chat.Id, "Ошибка: не удалось определить пользователя Telegram.", ct);
                return;
            }

            _registrations[msg.From.Id] = new RegistrationState { Stage = RegistrationStage.WaitingName };
            await _sender.SendMessageAsync(msg.Chat.Id, "Пользователь не найден. Давайте создадим аккаунт. Пожалуйста, введите ваше имя:", ct);
            return;
        }

    private async Task HandleHelpCmd(Common.DataContracts.User? member, Message msg, string arg, CancellationToken ct)
        {
            var helpText = "/start <access_code> - начать работу с ботом\n" +
                           "/help - показать это сообщение\n" +
                           "/me - показать ваш профиль";
            await _sender.SendMessageAsync(msg.Chat.Id, helpText, ct);
        }

    private async Task HandleMeCmd(Common.DataContracts.User? member, Message msg, string arg, CancellationToken ct)
        {
            // Show user's profile if exists
            try
            {
                var existing = member;
                if (existing == null)
                {
                    await _sender.SendMessageAsync(msg.Chat.Id, "Аккаунт не найден. Пожалуйста, зарегистрируйтесь сначала с помощью /start <access_code>", ct);
                    return;
                }

                await _sender.SendProfileCardAsync(msg.Chat.Id, existing, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user profile for /me command");
                await _sender.SendMessageAsync(msg.Chat.Id, "Ошибка при получении профиля. Попробуйте позже.", ct);
            }
        }

    private async Task HandleNextCmd(Common.DataContracts.User? member, Message msg, string arg, CancellationToken ct)
        {
            try
            {
                var next = await _apiClient.GetNextProfileAsync();
                if (next == null)
                {
                    await _sender.SendMessageAsync(msg.Chat.Id, "Профили не найдены.", ct);
                    return;
                }

                await _sender.SendProfileCardAsync(msg.Chat.Id, next, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching next profile for /next command");
                await _sender.SendMessageAsync(msg.Chat.Id, "Ошибка при получении следующего профиля. Попробуйте позже.", ct);
            }
        }

        private class RegistrationState
        {
            public RegistrationStage Stage { get; set; }
            public string? Name { get; set; }
            public string? TelegramAvatarFileId { get; set; }
            // Collected credentials during registration (optional)
            public string? Username { get; set; }
            public string? Password { get; set; }
        }

        private enum RegistrationStage
        {
            WaitingName,
            WaitingUsername,
            WaitingPassword,
            WaitingPhoto,
            Completed
        }
    }
}
