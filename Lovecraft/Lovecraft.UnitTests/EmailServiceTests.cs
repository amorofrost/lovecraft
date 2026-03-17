using Xunit;
using Lovecraft.Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lovecraft.UnitTests;

public class EmailServiceTests
{
    [Fact]
    public async Task NullEmailService_SendVerification_CompletesWithoutThrowing()
    {
        var svc = new NullEmailService(NullLogger<NullEmailService>.Instance);
        await svc.SendVerificationEmailAsync("user@example.com", "Alice", "token-123");
    }

    [Fact]
    public async Task NullEmailService_SendPasswordReset_CompletesWithoutThrowing()
    {
        var svc = new NullEmailService(NullLogger<NullEmailService>.Instance);
        await svc.SendPasswordResetEmailAsync("user@example.com", "Alice", "token-456");
    }
}
