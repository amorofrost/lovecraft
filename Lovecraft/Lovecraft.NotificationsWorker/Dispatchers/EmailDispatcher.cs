using Azure;
using Azure.Data.Tables;
using Lovecraft.Common;
using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Renderers;
using Microsoft.Extensions.Logging;

namespace Lovecraft.NotificationsWorker.Dispatchers;

/// <summary>
/// Real email dispatcher. Looks up user email + verified flag from the users table,
/// renders via IEmailDigestRenderer, and sends via IEmailSendClient (SendGrid wrapper).
/// Generates a 30-day signed unsubscribe token in every email footer.
/// </summary>
public class EmailDispatcher : IEmailDispatcher
{
    private static readonly TimeSpan UnsubscribeTokenLifetime = TimeSpan.FromDays(30);

    private readonly IEmailSendClient _client;
    private readonly TableClient _users;
    private readonly IEmailDigestRenderer _renderer;
    private readonly string _jwtSecret;
    private readonly ILogger<EmailDispatcher> _logger;

    public EmailDispatcher(
        IEmailSendClient client,
        TableClient users,
        IEmailDigestRenderer renderer,
        string jwtSecret,
        ILogger<EmailDispatcher> logger)
    {
        _client = client;
        _users = users;
        _renderer = renderer;
        _jwtSecret = jwtSecret;
        _logger = logger;
    }

    public async Task<DispatchResult> DispatchAsync(NotificationModel notification, CancellationToken ct)
    {
        var contact = await LookupContactAsync(notification.UserId, ct);
        if (contact is null) return DispatchResult.PermanentError;

        var unsubscribeToken = UnsubscribeToken.Generate(notification.UserId, _jwtSecret, DateTime.UtcNow + UnsubscribeTokenLifetime);
        var rendered = _renderer.RenderSingle(notification, unsubscribeToken);

        return await SendAsync(contact.Email, rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody, notification.NotificationId, ct);
    }

    public async Task<DispatchResult> DispatchDigestAsync(DigestModel digest, CancellationToken ct)
    {
        var contact = await LookupContactAsync(digest.UserId, ct);
        if (contact is null) return DispatchResult.PermanentError;

        var unsubscribeToken = UnsubscribeToken.Generate(digest.UserId, _jwtSecret, DateTime.UtcNow + UnsubscribeTokenLifetime);
        var rendered = _renderer.RenderDigest(digest, unsubscribeToken);

        return await SendAsync(contact.Email, rendered.Subject, rendered.HtmlBody, rendered.PlainTextBody,
            digestKey: $"digest-user-{digest.UserId}", ct);
    }

    private async Task<UserContactEntity?> LookupContactAsync(string userId, CancellationToken ct)
    {
        try
        {
            var pk = UserContactEntity.GetPartitionKey(userId);
            var resp = await _users.GetEntityAsync<UserContactEntity>(pk, userId, cancellationToken: ct);
            var contact = resp.Value;
            if (string.IsNullOrEmpty(contact.Email))
            {
                _logger.LogInformation("User {UserId} has no email on file; skipping email dispatch", userId);
                return null;
            }
            if (!contact.EmailVerified)
            {
                _logger.LogInformation("User {UserId} email not verified; skipping email dispatch", userId);
                return null;
            }
            return contact;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("User {UserId} not found in users table; skipping email dispatch", userId);
            return null;
        }
    }

    private async Task<DispatchResult> SendAsync(string toEmail, string subject, string html, string plain, string digestKey, CancellationToken ct)
    {
        try
        {
            var status = await _client.SendAsync(toEmail, subject, html, plain, ct);
            if (status >= 200 && status < 300) return DispatchResult.Delivered;
            if (status >= 500) return DispatchResult.RetryableError;
            return DispatchResult.PermanentError;
        }
        catch (TaskCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email send failed for {DigestKey} → {Email}; retryable", digestKey, toEmail);
            return DispatchResult.RetryableError;
        }
    }
}
