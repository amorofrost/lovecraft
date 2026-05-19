using System.Net;
using Azure;
using Azure.Data.Tables;
using Lovecraft.NotificationsWorker.Dispatchers;
using Lovecraft.NotificationsWorker.Entities;
using Lovecraft.NotificationsWorker.Models;
using Lovecraft.NotificationsWorker.Renderers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lovecraft.UnitTests.NotificationsWorker;

public class EmailDispatcherTests
{
    private static NotificationModel Sample() =>
        new("n1", "u1", "LikeReceived", "actor", "{\"likeId\":\"l1\"}", DateTime.UtcNow);

    private static (EmailDispatcher dispatcher, Mock<TableClient> users, Mock<IEmailSendClient> client) Build(
        UserContactEntity? contact, int sendStatus = 202)
    {
        var users = new Mock<TableClient>();
        if (contact is not null)
            users.Setup(t => t.GetEntityAsync<UserContactEntity>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(contact, new Mock<Response>().Object));
        else
            users.Setup(t => t.GetEntityAsync<UserContactEntity>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(404, "not found"));

        var client = new Mock<IEmailSendClient>();
        client.Setup(c => c.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sendStatus);

        var renderer = new EmailDigestRenderer("https://aloeve.club", "https://aloeve.club", NullLogger<EmailDigestRenderer>.Instance);
        var dispatcher = new EmailDispatcher(
            client.Object, users.Object, renderer,
            jwtSecret: "test-secret-32-chars-or-more-aaaa",
            NullLogger<EmailDispatcher>.Instance);
        return (dispatcher, users, client);
    }

    [Fact]
    public async Task Successful_send_returns_Delivered()
    {
        var (d, _, _) = Build(new UserContactEntity { Email = "u@example.com", EmailVerified = true }, sendStatus: 202);
        var r = await d.DispatchAsync(Sample(), CancellationToken.None);
        Assert.Equal(DispatchResult.Delivered, r);
    }

    [Fact]
    public async Task Missing_user_returns_PermanentError()
    {
        var (d, _, _) = Build(contact: null);
        var r = await d.DispatchAsync(Sample(), CancellationToken.None);
        Assert.Equal(DispatchResult.PermanentError, r);
    }

    [Fact]
    public async Task Empty_email_returns_PermanentError()
    {
        var (d, _, _) = Build(new UserContactEntity { Email = "", EmailVerified = true });
        var r = await d.DispatchAsync(Sample(), CancellationToken.None);
        Assert.Equal(DispatchResult.PermanentError, r);
    }

    [Fact]
    public async Task Unverified_email_returns_PermanentError()
    {
        var (d, _, _) = Build(new UserContactEntity { Email = "u@example.com", EmailVerified = false });
        var r = await d.DispatchAsync(Sample(), CancellationToken.None);
        Assert.Equal(DispatchResult.PermanentError, r);
    }

    [Fact]
    public async Task SendGrid_5xx_returns_RetryableError()
    {
        var (d, _, _) = Build(new UserContactEntity { Email = "u@example.com", EmailVerified = true }, sendStatus: 503);
        var r = await d.DispatchAsync(Sample(), CancellationToken.None);
        Assert.Equal(DispatchResult.RetryableError, r);
    }

    [Fact]
    public async Task SendGrid_4xx_returns_PermanentError()
    {
        var (d, _, _) = Build(new UserContactEntity { Email = "u@example.com", EmailVerified = true }, sendStatus: 400);
        var r = await d.DispatchAsync(Sample(), CancellationToken.None);
        Assert.Equal(DispatchResult.PermanentError, r);
    }

    [Fact]
    public async Task Digest_dispatch_uses_RenderDigest()
    {
        var (d, _, client) = Build(new UserContactEntity { Email = "u@example.com", EmailVerified = true }, sendStatus: 202);

        var digest = new DigestModel("u1", new List<NotificationModel> { Sample(), Sample() });
        var r = await d.DispatchDigestAsync(digest, CancellationToken.None);

        Assert.Equal(DispatchResult.Delivered, r);
        client.Verify(c => c.SendAsync(
            "u@example.com",
            It.Is<string>(s => s.Contains("2")),    // subject mentions count
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
