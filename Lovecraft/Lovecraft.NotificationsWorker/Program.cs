var builder = Host.CreateApplicationBuilder(args);

// DI registrations happen in Task 9.
// For now, just enough to build and run as a no-op:

var host = builder.Build();
await host.RunAsync();
