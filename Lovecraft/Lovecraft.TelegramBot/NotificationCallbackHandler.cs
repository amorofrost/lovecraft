using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Lovecraft.TelegramBot;

public class NotificationCallbackHandler
{
    private const string MutePrefix = "mute:";
    private const string MuteEndpoint = "/api/v1/internal/notifications/mute-type";

    private readonly HttpClient _http;
    private readonly string _serviceToken;
    private readonly ILogger<NotificationCallbackHandler> _logger;

    public NotificationCallbackHandler(HttpClient http, string serviceToken, ILogger<NotificationCallbackHandler> logger)
    {
        _http = http;
        _serviceToken = serviceToken;
        _logger = logger;
    }

    /// <summary>
    /// Returns true if the callback was a mute action (caller should answer the callback);
    /// false if the callback was unrecognized and should be ignored.
    /// </summary>
    public async Task<bool> HandleMuteCallbackAsync(long telegramUserId, string callbackData, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(callbackData) || !callbackData.StartsWith(MutePrefix, StringComparison.Ordinal))
            return false;

        var type = callbackData[MutePrefix.Length..];
        if (string.IsNullOrEmpty(type)) return false;

        var request = new HttpRequestMessage(HttpMethod.Post, MuteEndpoint);
        request.Headers.Add("X-Service-Token", _serviceToken);
        request.Content = JsonContent.Create(new
        {
            telegramUserId = telegramUserId.ToString(),
            type,
        });

        try
        {
            var resp = await _http.SendAsync(request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Mute callback for Telegram user {Id} type {Type} failed: {StatusCode}",
                    telegramUserId, type, resp.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mute callback for Telegram user {Id} type {Type} threw exception",
                telegramUserId, type);
        }

        return true;
    }
}
