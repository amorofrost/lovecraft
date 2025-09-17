namespace Lovecraft.TelegramBot;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http;

internal class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration(cfg =>
        {
            cfg.AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables();
        })
        .ConfigureLogging(l => l.AddConsole())
        .ConfigureServices((ctx, services) =>
        {
            var token = ctx.Configuration["Telegram:BotToken"]
                        ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
                        ?? throw new InvalidOperationException("Bot token missing.");
            services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(token));

            services.AddSingleton<IBotSender>(sp => new BotSender(sp.GetRequiredService<Telegram.Bot.ITelegramBotClient>()));
            services.AddSingleton<IBotHandler, BotMessageHandler>();
            services.AddHostedService<BotHostedService>();

            // Register an HttpClient that will present a client certificate when calling the WebAPI
            var clientCertPath = Environment.GetEnvironmentVariable("CLIENT_CERT_PATH")
                                 ?? ctx.Configuration["Certificates:ClientCertPath"];
            var clientCertPassword = Environment.GetEnvironmentVariable("CLIENT_CERT_PASSWORD")
                                       ?? ctx.Configuration["Certificates:ClientCertPassword"];

            services.AddHttpClient<Lovecraft.Common.ILovecraftApiClient, Lovecraft.Common.LovecraftApiClient>(client =>
            {
                client.BaseAddress = new Uri(ctx.Configuration["WebApi:BaseUrl"] ?? "https://webapi:5001/");
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                if (!string.IsNullOrEmpty(clientCertPath) && File.Exists(clientCertPath))
                {
                    var cert = string.IsNullOrEmpty(clientCertPassword)
                        ? new X509Certificate2(clientCertPath)
                        : new X509Certificate2(clientCertPath, clientCertPassword);
                    handler.ClientCertificates.Add(cert);
                }

                // Server certificate pinning: allowed thumbprints from configuration or environment
                var allowed = ctx.Configuration["WebApi:AllowedServerThumbprints"]
                              ?? Environment.GetEnvironmentVariable("ALLOWED_SERVER_THUMBPRINTS")
                              ?? string.Empty;

                var allowedSet = allowed.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(s => s.Replace(" ", string.Empty).ToUpperInvariant())
                                    .ToHashSet();

                if (allowedSet.Count == 0)
                {
                    // No pinning configured: use default server certificate validation
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }
                else
                {
                    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    {
                        if (cert is null) return false;
                        var thumb = (cert.Thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
                        return allowedSet.Contains(thumb);
                    };
                }

                return handler;
            });
        })
        .Build();

        await host.RunAsync();
    }
}