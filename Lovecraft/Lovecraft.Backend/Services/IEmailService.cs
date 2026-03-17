namespace Lovecraft.Backend.Services;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string toEmail, string name, string verificationToken);
    Task SendPasswordResetEmailAsync(string toEmail, string name, string resetToken);
}
