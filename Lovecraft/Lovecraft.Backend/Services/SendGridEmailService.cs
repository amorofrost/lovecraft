using SendGrid;
using SendGrid.Helpers.Mail;

namespace Lovecraft.Backend.Services;

public class SendGridEmailService : IEmailService
{
    private readonly SendGridClient _client;
    private readonly string _fromEmail;
    private readonly string _frontendBaseUrl;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(IConfiguration configuration, ILogger<SendGridEmailService> logger)
    {
        _logger = logger;
        var apiKey = configuration["SENDGRID_API_KEY"]
            ?? throw new InvalidOperationException("SENDGRID_API_KEY not configured");
        _fromEmail = configuration["FROM_EMAIL"] ?? "noreply@aloeband.ru";
        _frontendBaseUrl = configuration["FRONTEND_BASE_URL"] ?? "http://localhost:8080";
        _client = new SendGridClient(apiKey);
    }

    public async Task SendVerificationEmailAsync(string toEmail, string name, string verificationToken)
    {
        var link = $"{_frontendBaseUrl}/verify-email?token={verificationToken}";
        var msg = MailHelper.CreateSingleEmail(
            from: new EmailAddress(_fromEmail, "AloeVera"),
            to: new EmailAddress(toEmail, name),
            subject: "Verify your AloeVera email",
            plainTextContent: $"Hi {name},\n\nVerify your email: {link}\n\nThis link expires in 7 days.",
            htmlContent: $"<p>Hi {name},</p><p>Click to verify your email:<br><a href=\"{link}\">{link}</a></p><p>This link expires in 7 days.</p>"
        );

        var response = await _client.SendEmailAsync(msg);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            throw new InvalidOperationException($"SendGrid error {(int)response.StatusCode}: {body}");
        }

        _logger.LogInformation("Verification email sent to {Email}", toEmail);
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string name, string resetToken)
    {
        var link = $"{_frontendBaseUrl}/reset-password?token={resetToken}";
        var msg = MailHelper.CreateSingleEmail(
            from: new EmailAddress(_fromEmail, "AloeVera"),
            to: new EmailAddress(toEmail, name),
            subject: "Reset your AloeVera password",
            plainTextContent: $"Hi {name},\n\nReset your password: {link}\n\nThis link expires in 1 hour.",
            htmlContent: $"<p>Hi {name},</p><p>Click to reset your password:<br><a href=\"{link}\">{link}</a></p><p>This link expires in 1 hour. If you did not request this, ignore this email.</p>"
        );

        var response = await _client.SendEmailAsync(msg);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Body.ReadAsStringAsync();
            throw new InvalidOperationException($"SendGrid error {(int)response.StatusCode}: {body}");
        }

        _logger.LogInformation("Password reset email sent to {Email}", toEmail);
    }
}
