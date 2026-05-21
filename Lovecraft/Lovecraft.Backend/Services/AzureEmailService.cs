using Azure;
using Azure.Communication.Email;

namespace Lovecraft.Backend.Services;

public class AzureEmailService : IEmailService
{
    private readonly EmailClient _client;
    private readonly string _fromEmail;
    private readonly string _frontendBaseUrl;
    private readonly ILogger<AzureEmailService> _logger;

    public AzureEmailService(IConfiguration configuration, ILogger<AzureEmailService> logger)
    {
        _logger = logger;
        var connectionString = configuration["AZURE_COMMUNICATION_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("AZURE_COMMUNICATION_CONNECTION_STRING not configured");
        _fromEmail = configuration["FROM_EMAIL"] ?? "noreply@aloeband.ru";
        _frontendBaseUrl = configuration["FRONTEND_BASE_URL"] ?? "http://localhost:8080";
        _client = new EmailClient(connectionString);
    }

    public async Task SendVerificationEmailAsync(string toEmail, string name, string verificationToken)
    {
        var link = $"{_frontendBaseUrl}/verify-email?token={verificationToken}";

        var emailMessage = new EmailMessage(
            senderAddress: _fromEmail,
            content: new EmailContent("Verify your AloeVera email")
            {
                PlainText = $"Hi {name},\n\nVerify your email: {link}\n\nThis link expires in 7 days.",
                Html = $"<p>Hi {name},</p><p>Click to verify your email:<br><a href=\"{link}\">{link}</a></p><p>This link expires in 7 days.</p>"
            },
            recipients: new EmailRecipients(new List<EmailAddress>
            {
                new EmailAddress(toEmail, name)
            }));

        var operation = await _client.SendAsync(WaitUntil.Completed, emailMessage);

        if (operation.Value.Status == EmailSendStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Azure Communication Services email failed. OperationId: {operation.Id}");
        }

        _logger.LogInformation("Verification email sent to {Email} via Azure Communication Services", toEmail);
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string name, string resetToken)
    {
        var link = $"{_frontendBaseUrl}/reset-password?token={resetToken}";

        var emailMessage = new EmailMessage(
            senderAddress: _fromEmail,
            content: new EmailContent("Reset your AloeVera password")
            {
                PlainText = $"Hi {name},\n\nReset your password: {link}\n\nThis link expires in 1 hour.",
                Html = $"<p>Hi {name},</p><p>Click to reset your password:<br><a href=\"{link}\">{link}</a></p><p>This link expires in 1 hour. If you did not request this, ignore this email.</p>"
            },
            recipients: new EmailRecipients(new List<EmailAddress>
            {
                new EmailAddress(toEmail, name)
            }));

        var operation = await _client.SendAsync(WaitUntil.Completed, emailMessage);

        if (operation.Value.Status == EmailSendStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Azure Communication Services email failed. OperationId: {operation.Id}");
        }

        _logger.LogInformation("Password reset email sent to {Email} via Azure Communication Services", toEmail);
    }
}
