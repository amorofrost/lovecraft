namespace Lovecraft.TelegramBot;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

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

            services.AddHostedService<BotHostedService>();
        })
        .Build();

        await host.RunAsync();
    }
}