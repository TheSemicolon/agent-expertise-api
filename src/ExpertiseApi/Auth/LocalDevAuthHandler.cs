using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ExpertiseApi.Auth;

/// <summary>
/// Accepts an ad-hoc dev token format <c>Bearer dev:{tenant}:{scope1}+{scope2}</c>.
/// Registered only when <see cref="AuthMode.LocalDev"/> or <see cref="AuthMode.Hybrid"/>
/// is configured AND <see cref="IHostEnvironment.EnvironmentName"/> is Development.
/// <para>
/// The colon separator (rather than hyphen) keeps tenant names containing hyphens safe
/// (e.g. <c>team-alpha</c>). Scope shorthand (<c>read</c>/<c>draft</c>/<c>approve</c>/
/// <c>admin</c>) expands to the full <see cref="AuthConstants"/> scope strings; any other
/// value is passed through verbatim.
/// </para>
/// </summary>
internal class LocalDevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LocalDev";
    public const string TokenPrefix = "dev:";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var header = authHeader.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var raw = header["Bearer ".Length..].Trim();
        if (!raw.StartsWith(TokenPrefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var body = raw[TokenPrefix.Length..];
        var sepIndex = body.IndexOf(':', StringComparison.Ordinal);
        if (sepIndex <= 0)
        {
            Logger.LogWarning("LocalDev token rejected: malformed format (expected dev:tenant:scopes)");
            return Task.FromResult(AuthenticateResult.Fail("Malformed LocalDev token"));
        }

        var tenant = body[..sepIndex];
        var scopePart = body[(sepIndex + 1)..];
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(scopePart))
        {
            Logger.LogWarning("LocalDev token rejected: empty tenant or scope segment");
            return Task.FromResult(AuthenticateResult.Fail("Malformed LocalDev token"));
        }

        var scopes = scopePart
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ExpandShorthand);

        var expanded = JwtTenantContextEvents.ExpandScopeClosure(scopes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, $"localdev:{tenant}"),
            new("sub", $"localdev:{tenant}")
        };
        foreach (var scope in expanded)
            claims.Add(new Claim(AuthConstants.ScopeClaimType, scope));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);

        var tenantContext = new TenantContext(
            Tenant: tenant,
            Principal: principal,
            Agent: null,
            Scopes: expanded);
        Context.SetTenantContext(tenantContext);

        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static string ExpandShorthand(string scope) => scope switch
    {
        "read" => AuthConstants.ReadScope,
        "draft" => AuthConstants.WriteDraftScope,
        "approve" => AuthConstants.WriteApproveScope,
        "admin" => AuthConstants.AdminScope,
        _ => scope
    };
}
