namespace Lovecraft.WebAPI
{
    using System.Security.Cryptography.X509Certificates;
    using System.Linq;
    using System.IO;
    using Microsoft.AspNetCore.Authentication.Certificate;

    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Load CA cert path from configuration (appsettings.json or env)
            var caCertPath = builder.Configuration["Certificates:CaPath"];

            // Configure Kestrel to use HTTPS and require client certificates
            builder.WebHost.ConfigureKestrel(options =>
            {
                // Explicitly bind HTTP and HTTPS endpoints.
                // HTTP: listen on 0.0.0.0:5000 (optional)
                // HTTPS: listen on 0.0.0.0:5001 and require client certificates for mTLS
                options.ListenAnyIP(5000); // HTTP
                options.ListenAnyIP(5001, listenOptions =>
                {
                    listenOptions.UseHttps(httpsOptions =>
                    {
                        httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;
                    });
                });
            });

            // Prepare a logger factory to use inside events without building a new provider
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            // Add certificate authentication that validates client certificates
            builder.Services.AddAuthentication(
                CertificateAuthenticationDefaults.AuthenticationScheme)
                .AddCertificate(options =>
                {
                    // Optional: validate certificate using a fixed CA store (file)
                    options.RevocationMode = X509RevocationMode.NoCheck;

                    // If a CA bundle path is provided, trust only certs signed by that CA
                    if (!string.IsNullOrEmpty(caCertPath) && File.Exists(caCertPath))
                    {
                        var ca = new X509Certificate2(caCertPath);
                        options.AllowedCertificateTypes = CertificateTypes.Chained;
                        options.Events = new CertificateAuthenticationEvents
                        {
                            OnCertificateValidated = context =>
                            {
                                var logger = loggerFactory.CreateLogger<Program>();
                                try
                                {
                                    var clientCert = context.ClientCertificate;
                                    var clientThumb = (clientCert?.Thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
                                    logger.LogInformation("Validating client certificate with thumbprint {Thumb}", clientThumb);

                                    if (clientCert == null)
                                    {
                                        logger.LogWarning("No client certificate provided in request");
                                    }
                                    else
                                    {
                                        var chain = new X509Chain();
                                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                                        chain.ChainPolicy.ExtraStore.Add(ca);

                                        var isValid = chain.Build(clientCert);
                                        if (isValid)
                                        {
                                            var root = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                                            if (root.Thumbprint == ca.Thumbprint)
                                            {
                                                var allowed = builder.Configuration["Certificates:AllowedClientThumbprints"]
                                                              ?? Environment.GetEnvironmentVariable("ALLOWED_CLIENT_THUMBPRINTS")
                                                              ?? string.Empty;

                                                var allowedSet = allowed.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(s => s.Replace(" ", string.Empty).ToUpperInvariant())
                                                    .ToHashSet();

                                                if (allowedSet.Count == 0 || allowedSet.Contains(clientThumb))
                                                {
                                                    logger.LogInformation("Client certificate {Thumb} accepted", clientThumb);
                                                    context.Success();
                                                    return Task.CompletedTask;
                                                }
                                                else
                                                {
                                                    logger.LogWarning("Client certificate {Thumb} not in allowed list", clientThumb);
                                                }
                                            }
                                            else
                                            {
                                                logger.LogWarning("Client certificate {Thumb} chain does not terminate at configured CA", clientThumb);
                                            }
                                        }
                                        else
                                        {
                                            logger.LogWarning("Client certificate {Thumb} chain build failed", clientThumb);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "Exception while validating client certificate");
                                }

                                context.Fail("Client certificate validation failed.");
                                return Task.CompletedTask;
                            },
                            OnAuthenticationFailed = context =>
                            {
                                var logger = loggerFactory.CreateLogger<Program>();
                                logger.LogWarning(context.Exception, "Certificate authentication failed");
                                return Task.CompletedTask;
                            }
                        };
                    }
                    else
                    {
                        // Default: accept any valid certificate issued by a system-trusted CA
                        options.AllowedCertificateTypes = CertificateTypes.All;
                    }
                });

            // Add services to the container.
            builder.Services.AddControllers();

            var app = builder.Build();
            // Configure the HTTP request pipeline.
            app.UseHttpsRedirection();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            // Log the server addresses on startup for easier debugging.
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            try
            {
                var addressesFeature = app.Services.GetService<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
                if (addressesFeature != null && addressesFeature.Addresses.Any())
                {
                    foreach (var addr in addressesFeature.Addresses)
                    {
                        logger.LogInformation("Listening on {Address}", addr);
                    }
                }
                else if (app.Urls != null && app.Urls.Any())
                {
                    foreach (var url in app.Urls)
                    {
                        logger.LogInformation("Listening on {Url}", url);
                    }
                }
                else
                {
                    logger.LogInformation("Listening on default endpoints: http://0.0.0.0:5000 and https://0.0.0.0:5001");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enumerate server addresses");
            }

            app.Run();
        }
    }
}
