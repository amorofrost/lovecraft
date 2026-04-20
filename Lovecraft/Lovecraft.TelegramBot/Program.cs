using Lovecraft.TelegramBot;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<TelegramBotWorker>();

var host = builder.Build();
await host.RunAsync();
