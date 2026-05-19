using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Lovecraft.TelegramBot;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Lovecraft.UnitTests.TelegramBot;

public class NotificationCallbackHandlerTests
{
    private static (NotificationCallbackHandler handler, Mock<HttpMessageHandler> http) Build(HttpStatusCode responseCode = HttpStatusCode.NoContent)
    {
        var http = new Mock<HttpMessageHandler>();
        http.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(responseCode));

        var client = new HttpClient(http.Object) { BaseAddress = new Uri("http://backend:8080") };
        var handler = new NotificationCallbackHandler(client, serviceToken: "test-token", NullLogger<NotificationCallbackHandler>.Instance);
        return (handler, http);
    }

    [Fact]
    public async Task Mute_callback_posts_to_backend_with_service_token()
    {
        var (handler, http) = Build();

        await handler.HandleMuteCallbackAsync(telegramUserId: 555111, callbackData: "mute:messageReceived", CancellationToken.None);

        http.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.PathAndQuery == "/api/v1/internal/notifications/mute-type" &&
                req.Headers.GetValues("X-Service-Token").FirstOrDefault() == "test-token"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Malformed_callback_data_is_ignored_safely()
    {
        var (handler, http) = Build();

        await handler.HandleMuteCallbackAsync(telegramUserId: 555111, callbackData: "not-a-mute", CancellationToken.None);

        http.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task Empty_callback_data_is_ignored_safely()
    {
        var (handler, http) = Build();

        await handler.HandleMuteCallbackAsync(telegramUserId: 555111, callbackData: "", CancellationToken.None);

        http.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }
}
