namespace Lovecraft.NotificationsWorker.Dispatchers;

public interface IEmailSendClient
{
    /// <summary>
    /// Sends an email. Returns SendGrid HTTP status code.
    /// Implementations: SendGridEmailSendClient (production), or test stub.
    /// </summary>
    Task<int> SendAsync(string toEmail, string subject, string htmlBody, string plainTextBody, CancellationToken ct);
}
