namespace Lovecraft.WebAPI
{
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;
    using System.Linq;
    using System.IO;
    using Microsoft.Extensions.Logging;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.Certificate;

    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);



            // Prepare a logger factory to use inside events without building a new provider
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            // Load CA cert path from configuration (appsettings.json or env)
            var caCertPath = builder.Configuration["Certificates:CaPath"];
            X509Certificate2? caCert = null;
            if (!string.IsNullOrEmpty(caCertPath) && File.Exists(caCertPath))
            {
                caCert = new X509Certificate2(caCertPath);
            }

            // Read allowed client thumbprints early so Kestrel's TLS-level callback
            // can access them when validating client certificates during handshake.
            var allowedThumbsConfig = builder.Configuration["Certificates:AllowedClientThumbprints"]
                                      ?? Environment.GetEnvironmentVariable("ALLOWED_CLIENT_THUMBPRINTS")
                                      ?? string.Empty;
            var allowedThumbsSet = allowedThumbsConfig.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Replace(" ", string.Empty).ToUpperInvariant())
                .ToHashSet();

            // Configure Kestrel to use HTTPS and require client certificates
            builder.WebHost.ConfigureKestrel(options =>
            {
                // Explicitly bind HTTPS endpoint and only bind HTTP in Development.
                // HTTPS: listen on 0.0.0.0:5001 and require client certificates for mTLS
                var env = builder.Environment.EnvironmentName;
                // Log the environment and whether HTTP will be enabled so deployments
                // can be audited for accidentally exposing an HTTP endpoint.
                var kestrelLogger = loggerFactory.CreateLogger("KestrelEndpointConfig");
                var willEnableHttp = string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
                kestrelLogger.LogInformation("Kestrel endpoint config: Environment={Env}; EnableHttp5000={EnableHttp}", env, willEnableHttp);
                if (willEnableHttp)
                {
                    // In Development we enable the HTTP endpoint for convenience.
                    options.ListenAnyIP(5000); // HTTP (development only)
                }
                options.ListenAnyIP(5001, listenOptions =>
                {
                    listenOptions.UseHttps(httpsOptions =>
                    {
                        httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.RequireCertificate;

                        // Validate client certificates at TLS handshake time using our CA
                        // so the handshake doesn't fail before the authentication middleware runs.
                        httpsOptions.ClientCertificateValidation = (clientCert, chain, sslPolicyErrors) =>
                        {
                            var logger = loggerFactory.CreateLogger<Program>();
                            var chainToUse = chain ?? new X509Chain();
                            try
                            {
                                if (clientCert == null)
                                {
                                    logger.LogWarning("TLS: No client certificate presented");
                                    return false;
                                }

                                logger.LogDebug("TLS: Validating client certificate thumbprint {Thumb}", clientCert.Thumbprint);

                                // If we have a CA cert, use it to validate the chain
                                if (caCert != null)
                                {
                                    chainToUse.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                                    chainToUse.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                                    // Use custom trust so the provided CA is treated as a trusted root
                                    try
                                    {
                                        chainToUse.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                                        chainToUse.ChainPolicy.CustomTrustStore.Clear();
                                        chainToUse.ChainPolicy.CustomTrustStore.Add(caCert);
                                    }
                                    catch
                                    {
                                        // Older runtimes may not support TrustMode; fall back to ExtraStore
                                        chainToUse.ChainPolicy.ExtraStore.Add(caCert);
                                    }

                                    var built = chainToUse.Build(clientCert);
                                    if (!built)
                                    {
                                        logger.LogWarning("TLS: client certificate chain build failed for {Thumb}", clientCert.Thumbprint);
                                        return false;
                                    }
                                    var root = chainToUse.ChainElements[chainToUse.ChainElements.Count - 1].Certificate;
                                    if (root.Thumbprint != caCert.Thumbprint)
                                    {
                                        logger.LogWarning("TLS: client certificate chain does not terminate at configured CA for {Thumb}", clientCert.Thumbprint);
                                        return false;
                                    }
                                }

                                // If an allow-list is configured, ensure the client thumbprint is present
                                if (allowedThumbsSet.Count > 0)
                                {
                                    var clientThumb = (clientCert.Thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
                                    if (!allowedThumbsSet.Contains(clientThumb))
                                    {
                                        logger.LogWarning("TLS: client certificate {Thumb} not in allowed list", clientThumb);
                                        return false;
                                    }
                                }

                                logger.LogInformation("TLS: client certificate {Thumb} accepted at handshake", clientCert.Thumbprint);
                                return true;
                            }
                            catch (Exception ex)
                            {
                                var logger2 = loggerFactory.CreateLogger<Program>();
                                logger2.LogError(ex, "Exception while validating client certificate at TLS layer");
                                return false;
                            }
                        };
                    });
                });
            });

            

            // Register our own TLS-level certificate validation parameters and authentication handler
            var certValidationParams = new CertificateValidationParameters
            {
                CaCert = caCert,
                AllowedThumbprints = allowedThumbsSet
            };
            builder.Services.AddSingleton(certValidationParams);

            builder.Services.AddAuthentication("CertificateConnection")
                .AddScheme<AuthenticationSchemeOptions, ConnectionCertificateAuthenticationHandler>("CertificateConnection", options => { });

            // Add services to the container.
            builder.Services.AddControllers();
            // Register user repository (in-memory for now)
            builder.Services.AddSingleton<Repositories.IUserRepository, Repositories.InMemoryUserRepository>();

            var app = builder.Build();
            // Configure the HTTP request pipeline.
            app.UseHttpsRedirection();

            app.UseAuthentication();
            app.UseAuthorization();

            // Request logging middleware to help debug mTLS flows.
            // Logs the incoming request method/path and the client certificate thumbprint
            // (if present) so we can observe whether the client presented a certificate.
            app.Use(async (context, next) =>
            {
                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    var cert = context.Connection.ClientCertificate;
                    if (cert != null)
                    {
                        logger.LogInformation("Incoming request {Method} {Path} with client certificate thumbprint {Thumb}",
                            context.Request.Method, context.Request.Path, cert.Thumbprint);
                    }
                    else
                    {
                        logger.LogInformation("Incoming request {Method} {Path} with no client certificate",
                            context.Request.Method, context.Request.Path);
                    }
                }
                catch (Exception ex)
                {
                    var logger2 = app.Services.GetRequiredService<ILogger<Program>>();
                    logger2.LogWarning(ex, "Failed to log client certificate information");
                }

                await next();
            });

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
                    logger.LogInformation("Listening on default endpoints: https://0.0.0.0:5001{maybeHttp}", string.Equals(builder.Environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase) ? " and http://0.0.0.0:5000" : string.Empty);
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
