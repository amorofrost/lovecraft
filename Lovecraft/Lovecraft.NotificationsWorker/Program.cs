using Azure.Data.Tables;
using Lovecraft.NotificationsWorker;
using Lovecraft.NotificationsWorker.Dispatchers;
using Lovecraft.NotificationsWorker.Renderers;
using Lovecraft.NotificationsWorker.Services;
using Lovecraft.NotificationsWorker.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

// Explicit class avoids an implicit public `Program` type that conflicts with
// Lovecraft.Backend's `public partial class Program` when both are referenced from UnitTests.
internal sealed class NotificationsWorkerEntryPoint
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddLogging(b => b.AddSimpleConsole(o => { o.TimestampFormat = "yyyy-MM-dd HH:mm:ss "; o.IncludeScopes = false; }));

        var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.Error.WriteLine("AZURE_STORAGE_CONNECTION_STRING is not set; notifications worker cannot start.");
            return;
        }

        var useAzure = Environment.GetEnvironmentVariable("USE_AZURE_STORAGE")?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
        if (!useAzure)
        {
            Console.Error.WriteLine("USE_AZURE_STORAGE != true; notifications worker only runs in Azure mode. Exiting.");
            return;
        }

        var serviceClient = new TableServiceClient(connectionString);
        var notificationsTable = serviceClient.GetTableClient(TableNames.Notifications);
        var outboxTable = serviceClient.GetTableClient(TableNames.NotificationsOutbox);
        var preferencesTable = serviceClient.GetTableClient(TableNames.NotificationPreferences);

        // Tables are created by the backend on startup — worker assumes they exist.
        // Defensive: CreateIfNotExists is idempotent.
        notificationsTable.CreateIfNotExists();
        outboxTable.CreateIfNotExists();
        preferencesTable.CreateIfNotExists();

        // outbox + preferences are accessed via the processors that take them explicitly via captured closures.
        // notificationsTable is also captured directly — do NOT register it via DI to avoid type ambiguity.

        // Hoist usersTable so both Telegram and Email blocks can capture it via closure.
        // Do NOT call CreateIfNotExists — the backend owns the users table.
        var usersTable = serviceClient.GetTableClient(TableNames.Users);

        var telegramBotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
        if (string.IsNullOrEmpty(telegramBotToken))
        {
            var startupLogger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<NotificationsWorkerEntryPoint>>();
            startupLogger.LogWarning("TELEGRAM_BOT_TOKEN is not set; using StubTelegramDispatcher. Telegram notifications will be silently dropped.");
            builder.Services.AddSingleton<ITelegramDispatcher, StubTelegramDispatcher>();
        }
        else
        {
            builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramBotToken));
            builder.Services.AddSingleton(usersTable);
            builder.Services.AddSingleton<ITelegramRateLimiter, TelegramRateLimiter>();
            builder.Services.AddSingleton<ITelegramMessageRenderer, TelegramMessageRenderer>();
            builder.Services.AddSingleton<ITelegramSendClient, TelegramSendClient>();
            builder.Services.AddSingleton<ITelegramDispatcher>(sp =>
                new TelegramDispatcher(
                    sp.GetRequiredService<ITelegramSendClient>(),
                    sp.GetRequiredService<TableClient>(),
                    sp.GetRequiredService<ITelegramMessageRenderer>(),
                    sp.GetRequiredService<ITelegramRateLimiter>(),
                    sp.GetRequiredService<ILogger<TelegramDispatcher>>()));
        }

        var sendGridApiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
        var jwtSecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
        if (!string.IsNullOrEmpty(sendGridApiKey) && !string.IsNullOrEmpty(jwtSecretKey))
        {
            var fromEmail = Environment.GetEnvironmentVariable("FROM_EMAIL") ?? "noreply@aloeband.ru";
            var frontendBaseUrl = Environment.GetEnvironmentVariable("FRONTEND_BASE_URL") ?? "https://aloeve.club";

            builder.Services.AddSingleton<IEmailSendClient>(
                new SendGridEmailSendClient(sendGridApiKey, fromEmail));
            builder.Services.AddSingleton<IEmailDigestRenderer>(sp =>
                new EmailDigestRenderer(frontendBaseUrl, frontendBaseUrl, sp.GetRequiredService<ILogger<EmailDigestRenderer>>()));
            builder.Services.AddSingleton<IEmailDispatcher>(sp =>
                new EmailDispatcher(
                    sp.GetRequiredService<IEmailSendClient>(),
                    usersTable,
                    sp.GetRequiredService<IEmailDigestRenderer>(),
                    jwtSecretKey,
                    sp.GetRequiredService<ILogger<EmailDispatcher>>()));
        }
        else
        {
            var startupLogger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<NotificationsWorkerEntryPoint>>();
            startupLogger.LogWarning("SENDGRID_API_KEY or JWT_SECRET_KEY is not set; using StubEmailDispatcher. Email notifications will be silently dropped.");
            builder.Services.AddSingleton<IEmailDispatcher, StubEmailDispatcher>();
        }

        builder.Services.AddSingleton<IOutboxProcessor>(sp =>
            new OutboxProcessor(
                outboxTable, notificationsTable,
                sp.GetRequiredService<ITelegramDispatcher>(),
                sp.GetRequiredService<IEmailDispatcher>(),
                sp.GetRequiredService<ILogger<OutboxProcessor>>()));

        builder.Services.AddSingleton<IDigestProcessor>(sp =>
            new DigestProcessor(
                outboxTable, notificationsTable, preferencesTable,
                sp.GetRequiredService<ITelegramDispatcher>(),
                sp.GetRequiredService<IEmailDispatcher>(),
                sp.GetRequiredService<ILogger<DigestProcessor>>()));

        builder.Services.AddSingleton<IOutboxJanitor>(sp =>
            new OutboxJanitor(outboxTable, notificationsTable, sp.GetRequiredService<ILogger<OutboxJanitor>>()));

        builder.Services.AddHostedService<DispatcherWorker>();
        builder.Services.AddHostedService<DigestWorker>();
        builder.Services.AddHostedService<JanitorWorker>();

        var host = builder.Build();
        await host.RunAsync();
    }
}
