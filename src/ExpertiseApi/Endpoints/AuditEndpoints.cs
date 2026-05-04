using ExpertiseApi.Auth;
using ExpertiseApi.Data;
using ExpertiseApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace ExpertiseApi.Endpoints;

/// <summary>
/// Cross-tenant audit log queries. Admin-only per ADR-003 line 32. Tenant-scoped audit
/// reads for non-admin reviewers are deferred (tracked separately).
/// </summary>
internal static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/audit")
            .WithTags("Audit")
            .RequireAuthorization(AuthConstants.Policies.AdminAccess);

        group.MapGet("/", ListAudit);

        return group;
    }

    private static async Task<IResult> ListAudit(
        IExpertiseRepository repo,
        [FromQuery] Guid? entryId,
        [FromQuery] string? principal,
        [FromQuery] AuditAction? action,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 50,
        [FromQuery(Name = "afterTimestamp")] DateTime? afterTimestamp = null,
        [FromQuery(Name = "afterId")] Guid? afterId = null,
        CancellationToken ct = default)
    {
        // Cursor pagination requires both halves of the keyset; if only one is provided
        // we treat the cursor as absent rather than producing surprising results.
        var hasCursor = afterTimestamp is not null && afterId is not null;
        var filter = new AuditLogFilter(
            EntryId: entryId,
            Principal: principal,
            Action: action,
            From: from,
            To: to,
            Limit: limit,
            AfterTimestamp: hasCursor ? afterTimestamp : null,
            AfterId: hasCursor ? afterId : null);

        var rows = await repo.ListAuditAsync(filter, ct);
        return Results.Ok(rows);
    }
}
