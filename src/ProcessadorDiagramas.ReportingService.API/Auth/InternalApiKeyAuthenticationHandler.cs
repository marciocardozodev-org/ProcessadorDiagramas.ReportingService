using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ProcessadorDiagramas.ReportingService.Infrastructure.Configuration;

namespace ProcessadorDiagramas.ReportingService.API.Auth;

public sealed class InternalApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "InternalApiKey";

    private readonly InternalApiOptions _options;

    public InternalApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> authOptions,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<InternalApiOptions> internalApiOptions)
        : base(authOptions, logger, encoder)
    {
        _options = internalApiOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return Task.FromResult(AuthenticateResult.Fail("Internal API key is not configured."));

        if (!Request.Headers.TryGetValue(_options.ApiKeyHeaderName, out var providedApiKey)
            || !string.Equals(providedApiKey.ToString(), _options.ApiKey, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid internal API key."));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "internal-api") };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
