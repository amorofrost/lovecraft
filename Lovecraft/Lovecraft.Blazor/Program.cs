using Lovecraft.Blazor.Components;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Lovecraft.Blazor.Services;

namespace Lovecraft.Blazor;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Configure certificate pinning for HttpClient
        ConfigureCertificatePinning(builder);

    builder.Services.AddScoped<ProtectedLocalStorage>();
    builder.Services.AddScoped<AuthService>();

        var app = builder.Build();

        // Initialize AuthService from storage (best-effort)
        try
        {
            var scope = app.Services.CreateScope();
            var auth = scope.ServiceProvider.GetService<AuthService>();
            if (auth != null)
            {
                // fire-and-forget initialization
                _ = auth.InitializeAsync();
            }
        }
        catch
        {
            // ignore
        }

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }

    private static void ConfigureCertificatePinning(WebApplicationBuilder builder)
    {
        // Configure HttpClient for calling the WebAPI and present client certificate (PFX) from /app/certs
        string webApiBase = builder.Configuration["WebApi:BaseUrl"] ?? "https://lovecraft-webapi:5001/";
        string blzClientPath = builder.Configuration["BLZ_CLIENT_CERT_PATH"] ?? Environment.GetEnvironmentVariable("BLZ_CLIENT_CERT_PATH") ?? string.Empty;
        string blzClientPassword = builder.Configuration["BLZ_CLIENT_CERT_PASSWORD"] ?? Environment.GetEnvironmentVariable("BLZ_CLIENT_CERT_PASSWORD") ?? string.Empty;
        X509Certificate2? clientCert = null;
        
        if (File.Exists(blzClientPath))
        {
            try
            {
                clientCert = new X509Certificate2(blzClientPath, blzClientPassword ?? string.Empty, X509KeyStorageFlags.EphemeralKeySet);
            }
            catch
            {
                // ignore failures; leave clientCert null
            }
        }

        // Register the typed API client so pages can inject ILovecraftApiClient
        builder.Services.AddHttpClient<Lovecraft.Common.Interfaces.ILovecraftApiClient, Lovecraft.Common.Services.LovecraftApiClient>(client =>
        {
            client.BaseAddress = new Uri(webApiBase);
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler();
            if (clientCert != null)
            {
                handler.ClientCertificates.Add(clientCert);
            }

            // TODO: server certificate pinning
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            return handler;
        });
    }
}
