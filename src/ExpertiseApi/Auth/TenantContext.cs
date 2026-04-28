using System.Security.Claims;

namespace ExpertiseApi.Auth;

/// <summary>
/// Authenticated request context. Populated by the authentication pipeline (JWT, API key,
/// or LocalDev) and consumed by endpoints, repositories, and the audit log.
/// <para>
/// <see cref="Tenant"/> is null when the principal authenticated successfully but did not
/// map to any configured tenant — the authorization layer turns this into 403.
/// </para>
/// <para>
/// <see cref="Scopes"/> is the expanded closure: a token carrying only <c>expertise.admin</c>
/// has all four scopes present (admin ⊇ approve ⊇ draft ⊇ read).
/// </para>
/// </summary>
public sealed record TenantContext(
    string? Tenant,
    ClaimsPrincipal Principal,
    string? Agent,
    IReadOnlySet<string> Scopes);

public static class TenantContextHttpExtensions
{
    public static TenantContext? GetTenantContext(this HttpContext ctx) =>
        ctx.Features.Get<TenantContext>();

    public static TenantContext RequireTenantContext(this HttpContext ctx) =>
        ctx.GetTenantContext()
        ?? throw new InvalidOperationException(
            "TenantContext is not set on HttpContext. Ensure the authentication pipeline ran.");

    public static void SetTenantContext(this HttpContext ctx, TenantContext value) =>
        ctx.Features.Set(value);
}
