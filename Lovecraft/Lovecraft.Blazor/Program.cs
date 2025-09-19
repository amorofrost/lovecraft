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
string pfxPath = "/app/certs/client.pfx";
X509Certificate2? clientCert = null;
if (File.Exists(pfxPath))
{
    try
    {
        // Use EphemeralKeySet where possible to avoid requiring key storage permissions in containers
        clientCert = new X509Certificate2(pfxPath, "", X509KeyStorageFlags.EphemeralKeySet);
    }
    catch
    {
        // best-effort; leave clientCert null if loading fails
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
