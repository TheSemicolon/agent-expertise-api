using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ExpertiseApi.Auth;

public class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedKey = configuration["Auth:ApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
        {
            Logger.LogWarning("Authentication failed: {Reason}", "API key not configured on server");
            return Task.FromResult(AuthenticateResult.Fail("API key not configured"));
        }

        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            Logger.LogWarning("Authentication failed: {Reason}", "missing Authorization header");
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header"));
        }

        var header = authHeader.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogWarning("Authentication failed: {Reason}", "invalid Authorization scheme");
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization scheme"));
        }

        var providedKey = header["Bearer ".Length..].Trim();
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, providedHash))
        {
            Logger.LogWarning("Authentication failed: {Reason}", "invalid API key");
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "api-client"),
            new Claim(AuthConstants.ScopeClaimType, AuthConstants.ReadScope),
            new Claim(AuthConstants.ScopeClaimType, AuthConstants.WriteScope)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
