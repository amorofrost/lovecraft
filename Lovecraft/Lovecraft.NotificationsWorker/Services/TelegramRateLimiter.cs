using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Services;

public class TelegramRateLimiter : ITelegramRateLimiter, IDisposable
{
    private static readonly TimeSpan PerChatCooldown = TimeSpan.FromSeconds(1);
    private const int MaxGlobalConcurrency = 25;

    private readonly SemaphoreSlim _globalSemaphore = new(MaxGlobalConcurrency, MaxGlobalConcurrency);
    private readonly ConcurrentDictionary<string, DateTime> _lastSendUtc = new();
    private readonly ILogger<TelegramRateLimiter> _logger;

    public TelegramRateLimiter(ILogger<TelegramRateLimiter> logger)
    {
        _logger = logger;
    }

    public async Task AcquireAsync(string chatId, CancellationToken ct)
    {
        await _globalSemaphore.WaitAsync(ct);
        try
        {
            if (_lastSendUtc.TryGetValue(chatId, out var last))
            {
                var elapsed = DateTime.UtcNow - last;
                if (elapsed < PerChatCooldown)
                {
                    var delay = PerChatCooldown - elapsed;
                    _logger.LogDebug("Rate limiting chat {ChatId}: waiting {Delay}ms", chatId, delay.TotalMilliseconds);
                    await Task.Delay(delay, ct);
                }
            }
            _lastSendUtc[chatId] = DateTime.UtcNow;
        }
        finally
        {
            _globalSemaphore.Release();
        }
    }

    public void Dispose() => _globalSemaphore.Dispose();
}
