using SendGrid;
using SendGrid.Helpers.Mail;

namespace Lovecraft.NotificationsWorker.Dispatchers;

/// <summary>
/// Production implementation: delegates to the SendGrid SDK.
/// Registered in DI when SENDGRID_API_KEY is present (Task 6).
/// </summary>
public class SendGridEmailSendClient : IEmailSendClient
{
    private readonly ISendGridClient _client;
    private readonly EmailAddress _from;

    public SendGridEmailSendClient(string apiKey, string fromEmail, string fromName = "AloeVera")
    {
        _client = new SendGridClient(apiKey);
        _from = new EmailAddress(fromEmail, fromName);
    }

    public async Task<int> SendAsync(string toEmail, string subject, string htmlBody, string plainTextBody, CancellationToken ct)
    {
        var msg = MailHelper.CreateSingleEmail(
            _from,
            new EmailAddress(toEmail),
            subject,
            plainTextBody,
            htmlBody);
        var response = await _client.SendEmailAsync(msg, ct);
        return (int)response.StatusCode;
    }
}
