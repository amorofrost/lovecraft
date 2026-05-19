using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Renderers;
using Lovecraft.NotificationsWorker.Services;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;

namespace Lovecraft.NotificationsWorker.Dispatchers;

/// <summary>
/// Real Telegram channel dispatcher. Looks up the user's Telegram chat id from the users table,
/// renders the notification via ITelegramMessageRenderer, and sends via ITelegramSendClient.
/// Uses ITelegramSendClient (a thin wrapper) instead of ITelegramBotClient directly so that
/// unit tests can mock the send call without relying on SDK internal request types.
/// </summary>
public class TelegramDispatcher : ITelegramDispatcher
{
    private readonly ITelegramSendClient _sendClient;
    private readonly TableClient _users;
    private readonly ITelegramMessageRenderer _renderer;
    private readonly ITelegramRateLimiter _rateLimiter;
    private readonly ILogger<TelegramDispatcher> _logger;

    public TelegramDispatcher(
        ITelegramSendClient sendClient,
        TableClient users,
        ITelegramMessageRenderer renderer,
        ITelegramRateLimiter rateLimiter,
        ILogger<TelegramDispatcher> logger)
    {
        _sendClient = sendClient;
        _users = users;
        _renderer = renderer;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct)
    {
        // Step 1: Look up user's Telegram chat id from the users table
        string? telegramUserId = null;
        try
        {
            var pk = UserContactEntity.GetPartitionKey(notification.UserId);
            var entity = await _users.GetEntityAsync<UserContactEntity>(pk, notification.UserId, cancellationToken: ct);
            telegramUserId = entity.Value.TelegramUserId;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("User {UserId} not found in users table while dispatching Telegram notification {NotificationId}",
                notification.UserId, notification.NotificationId);
            return DispatchResult.PermanentError;
        }

        // Step 2: No Telegram account linked → dead-letter immediately
        if (string.IsNullOrEmpty(telegramUserId))
        {
            _logger.LogWarning("User {UserId} has no Telegram account linked; cannot dispatch notification {NotificationId}",
                notification.UserId, notification.NotificationId);
            return DispatchResult.PermanentError;
        }

        // Step 3: Acquire rate limiter slot for this chat
        await _rateLimiter.AcquireAsync(telegramUserId, ct);

        // Step 4: Render notification as Telegram HTML + inline keyboard
        var (html, keyboard) = _renderer.Render(notification);

        // Step 5: Send
        try
        {
            await _sendClient.SendAsync(telegramUserId, html, keyboard, ct);
            return DispatchResult.Delivered;
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            // Bot was blocked by the user — dead-letter. Auto-disabling user's Telegram prefs is
            // deferred to a follow-up (would require a back-channel call to the backend).
            _logger.LogInformation(
                "Telegram bot blocked by user {UserId} (Telegram id {TelegramId}); dead-lettering notification {NotificationId}. " +
                "Note: user's Telegram prefs are NOT auto-disabled in Phase D — tracked in follow-up.",
                notification.UserId, telegramUserId, notification.NotificationId);
            return DispatchResult.PermanentError;
        }
        catch (ApiRequestException ex)
        {
            _logger.LogWarning("Telegram API error {Code}: {Message} (notification {NotificationId})",
                ex.ErrorCode, ex.Message, notification.NotificationId);
            return DispatchResult.PermanentError;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error dispatching Telegram notification {NotificationId}; will retry",
                notification.NotificationId);
            return DispatchResult.RetryableError;
        }
        catch (TaskCanceledException)
        {
            // Shutdown signal — re-throw so the worker stops cleanly
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error dispatching Telegram notification {NotificationId}",
                notification.NotificationId);
            return DispatchResult.RetryableError;
        }
    }
}
