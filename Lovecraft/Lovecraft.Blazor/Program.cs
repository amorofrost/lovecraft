using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Configure HttpClient for calling the WebAPI and present client certificate (PFX) from /app/certs
string webApiBase = builder.Configuration["WebApi:BaseUrl"] ?? "https://lovecraft-webapi:5001/";
// Determine which client certificate to use for outbound WebAPI calls.
// Prefer Blazor-specific client cert (BLZ_CLIENT_CERT_PATH) if provided; otherwise fall back to common client.pfx.
string blzClientPath = builder.Configuration["BLZ_CLIENT_CERT_PATH"] ?? Environment.GetEnvironmentVariable("BLZ_CLIENT_CERT_PATH") ?? string.Empty;
string blzClientPassword = builder.Configuration["BLZ_CLIENT_CERT_PASSWORD"] ?? Environment.GetEnvironmentVariable("BLZ_CLIENT_CERT_PASSWORD") ?? string.Empty;
string defaultClientPath = "/app/certs/client.pfx";
X509Certificate2? clientCert = null;
string chosenClientPath = string.IsNullOrEmpty(blzClientPath) ? defaultClientPath : blzClientPath;
if (File.Exists(chosenClientPath))
{
    try
    {
        clientCert = new X509Certificate2(chosenClientPath, blzClientPassword ?? string.Empty, X509KeyStorageFlags.EphemeralKeySet);
    }
    catch
    {
        // ignore failures; leave clientCert null
    }
}

builder.Services.AddHttpClient("webapi", client =>
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
        // For local development with self-signed certs we accept any server certificate.
        // In production you should validate server certificates properly.
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        return handler;
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

// Expose the implicit Program class for WebApplicationFactory in integration tests
public partial class Program { }
