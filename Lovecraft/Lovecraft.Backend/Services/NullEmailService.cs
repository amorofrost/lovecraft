namespace Lovecraft.Backend.Services;

public class NullEmailService : IEmailService
{
    private readonly ILogger<NullEmailService> _logger;

    public NullEmailService(ILogger<NullEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendVerificationEmailAsync(string toEmail, string name, string verificationToken)
    {
        // Link hardcoded to localhost — NullEmailService takes no IConfiguration by design.
        // When SENDGRID_API_KEY is set, SendGridEmailService uses FRONTEND_BASE_URL from config.
        _logger.LogInformation(
            "[NullEmailService] Verification email suppressed. To: {Email}, Token: {Token}, Link: http://localhost:8080/verify-email?token={Token}",
            toEmail, verificationToken, verificationToken);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string name, string resetToken)
    {
        _logger.LogInformation(
            "[NullEmailService] Password reset email suppressed. To: {Email}, Token: {Token}, Link: http://localhost:8080/reset-password?token={Token}",
            toEmail, resetToken, resetToken);
        return Task.CompletedTask;
    }
}
