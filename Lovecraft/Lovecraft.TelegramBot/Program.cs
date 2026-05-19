using Lovecraft.TelegramBot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Explicit class avoids an implicit public `Program` type that conflicts with
// Lovecraft.Backend's `public partial class Program` when both are referenced from UnitTests.
internal sealed class TelegramBotEntryPoint
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        var serviceToken = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_TOKEN");
        var backendUrl = Environment.GetEnvironmentVariable("BACKEND_INTERNAL_URL") ?? "http://backend:8080";

        if (!string.IsNullOrEmpty(serviceToken))
        {
            builder.Services.AddSingleton(sp =>
            {
                var client = new HttpClient { BaseAddress = new Uri(backendUrl) };
                return new NotificationCallbackHandler(client, serviceToken, sp.GetRequiredService<ILogger<NotificationCallbackHandler>>());
            });
        }

        builder.Services.AddHostedService<TelegramBotWorker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}
