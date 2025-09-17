namespace Lovecraft.WebAPI
{
    using System;
    using System.Linq;
    using System.Security.Claims;
    using System.Security.Cryptography.X509Certificates;
    using System.Text.Encodings.Web;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.Extensions.Hosting;

    public sealed class CertificateValidationParameters
    {
        public X509Certificate2? CaCert { get; set; }
        public System.Collections.Generic.HashSet<string> AllowedThumbprints { get; set; } = new();
    }

    public class ConnectionCertificateAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly CertificateValidationParameters _params;
        private readonly IHostEnvironment _env;
        public ConnectionCertificateAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            CertificateValidationParameters @params,
            IHostEnvironment env)
            : base(options, logger, encoder, clock)
        {
            _params = @params ?? throw new ArgumentNullException(nameof(@params));
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            try
            {
                var cert = Context.Connection.ClientCertificate;
                if (cert == null)
                {
                    // In Development (or test) environments where TestServer is used, there
                    // is no TLS layer and no client certificate. Allow authentication to
                    // succeed to make integration tests simpler.
                    if (_env.IsDevelopment())
                    {
                        Logger.LogDebug("Handler: Development environment - no client certificate present, issuing dev principal");
                        var claimsDev = new[] { new Claim(ClaimTypes.Name, "dev-client"), new Claim("thumbprint", string.Empty) };
                        var identityDev = new ClaimsIdentity(claimsDev, Scheme.Name);
                        var principalDev = new ClaimsPrincipal(identityDev);
                        var ticketDev = new AuthenticationTicket(principalDev, Scheme.Name);
                        return Task.FromResult(AuthenticateResult.Success(ticketDev));
                    }

                    return Task.FromResult(AuthenticateResult.Fail("No client certificate provided."));
                }

                var logger = Logger;
                logger.LogDebug("Handler: validating client certificate thumbprint {Thumb}", cert.Thumbprint);

                var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

                if (_params.CaCert != null)
                {
                    try
                    {
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Clear();
                        chain.ChainPolicy.CustomTrustStore.Add(_params.CaCert);
                    }
                    catch
                    {
                        chain.ChainPolicy.ExtraStore.Add(_params.CaCert);
                    }
                }

                var built = chain.Build(cert);
                if (!built)
                {
                    logger.LogWarning("Handler: client certificate chain build failed for {Thumb}", cert.Thumbprint);
                    return Task.FromResult(AuthenticateResult.Fail("Client certificate chain build failed."));
                }

                if (_params.AllowedThumbprints != null && _params.AllowedThumbprints.Count > 0)
                {
                    var thumb = (cert.Thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
                    if (!_params.AllowedThumbprints.Contains(thumb))
                    {
                        logger.LogWarning("Handler: client certificate {Thumb} not in allow list", thumb);
                        return Task.FromResult(AuthenticateResult.Fail("Client certificate not allowed."));
                    }
                }

                var claims = new[] { new Claim(ClaimTypes.Name, cert.Subject), new Claim("thumbprint", cert.Thumbprint ?? string.Empty) };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception during certificate authentication");
                return Task.FromResult(AuthenticateResult.Fail("Exception during certificate authentication."));
            }
        }
    }
}
