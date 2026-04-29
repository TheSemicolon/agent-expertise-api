using ExpertiseApi.Auth;
using ExpertiseApi.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace ExpertiseApi.Data;

public class ExpertiseRepository(ExpertiseDbContext db, ILogger<ExpertiseRepository> logger) : IExpertiseRepository
{
    /// <summary>
    /// Builds the tenant predicate per ADR-001: a row is visible if its <c>Tenant</c>
    /// matches the caller's, or if it is in the cross-tenant <c>shared</c> namespace.
    /// </summary>
    private static IQueryable<ExpertiseEntry> ApplyTenantFilter(
        IQueryable<ExpertiseEntry> query, TenantContext ctx)
    {
        var tenant = RequireTenant(ctx);
        return query.Where(e => e.Tenant == tenant || e.Tenant == "shared");
    }

    /// <summary>
    /// Read filter that gates draft visibility. By default reads are restricted to
    /// <see cref="ReviewState.Approved"/> entries. <paramref name="includeDrafts"/> is
    /// only honored at the endpoint layer when the caller carries
    /// <see cref="AuthConstants.WriteApproveScope"/>.
    /// </summary>
    private static IQueryable<ExpertiseEntry> ApplyReviewStateFilter(
        IQueryable<ExpertiseEntry> query, bool includeDrafts)
    {
        return includeDrafts
            ? query
            : query.Where(e => e.ReviewState == ReviewState.Approved);
    }

    /// <summary>
    /// Defensive guard. If the auth pipeline produced a <see cref="TenantContext"/> with a
    /// null <c>Tenant</c> (unmapped principal), the authorization handler should have
    /// returned 403 before we reached the repository — but if anything slips through, fail
    /// loud rather than running an unbounded query against the full table.
    /// </summary>
    private static string RequireTenant(TenantContext ctx) =>
        ctx.Tenant ?? throw new InvalidOperationException(
            "Repository invoked with TenantContext.Tenant=null. The authorization pipeline " +
            "must reject unmapped principals before any repository call.");

    public async Task<ExpertiseEntry?> GetByIdAsync(Guid id, TenantContext ctx, CancellationToken ct)
    {
        // FindAsync would short-circuit through the identity map and bypass the tenant
        // filter; explicit Where + FirstOrDefaultAsync keeps every read tenant-scoped.
        return await ApplyTenantFilter(db.ExpertiseEntries.AsQueryable(), ctx)
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<ExpertiseEntry>> ListAsync(
        TenantContext ctx,
        string? domain,
        List<string>? tags,
        EntryType? entryType,
        Severity? severity,
        bool includeDrafts,
        bool includeDeprecated,
        CancellationToken ct)
    {
        var query = ApplyTenantFilter(db.ExpertiseEntries.AsQueryable(), ctx);
        query = ApplyReviewStateFilter(query, includeDrafts);

        if (!includeDeprecated)
            query = query.Where(e => e.DeprecatedAt == null);

        if (domain is not null)
            query = query.Where(e => e.Domain == domain);

        if (entryType is not null)
            query = query.Where(e => e.EntryType == entryType);

        if (severity is not null)
            query = query.Where(e => e.Severity == severity);

        if (tags is { Count: > 0 })
            query = query.Where(e => tags.All(t => e.Tags.Contains(t)));

        return await query.OrderByDescending(e => e.UpdatedAt).ToListAsync(ct);
    }

    public async Task<ExpertiseEntry> CreateAsync(ExpertiseEntry entry, TenantContext ctx, CancellationToken ct)
    {
        // Defensive: ensure the entry's Tenant matches the caller's. BuildEntry wires this
        // from the same TenantContext, but verifying here closes the loop against any
        // future code path that constructs an entry with a request-supplied tenant.
        var callerTenant = RequireTenant(ctx);
        if (!string.Equals(entry.Tenant, callerTenant, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Entry tenant '{entry.Tenant}' does not match caller tenant '{callerTenant}'.");

        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;

        db.ExpertiseEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        return entry;
    }

    public async Task<ExpertiseEntry?> UpdateAsync(Guid id, TenantContext ctx, Func<ExpertiseEntry, Task> applyUpdates, CancellationToken ct)
    {
        var entry = await ApplyTenantFilter(db.ExpertiseEntries.AsQueryable(), ctx)
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync(ct);
        if (entry is null)
            return null;

        await applyUpdates(entry);
        entry.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<bool> SoftDeleteAsync(Guid id, TenantContext ctx, CancellationToken ct)
    {
        var entry = await ApplyTenantFilter(db.ExpertiseEntries.AsQueryable(), ctx)
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync(ct);
        if (entry is null)
            return false;

        entry.DeprecatedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<ExpertiseEntry>> KeywordSearchAsync(string query, TenantContext ctx, bool includeDrafts, bool includeDeprecated, CancellationToken ct)
    {
        // Tenant + ReviewState + DeprecatedAt filters live inside the raw SQL alongside
        // ORDER BY ts_rank because composing LINQ Where on top of FromSqlInterpolated
        // wraps the original query in a subquery — the planner may then drop the inner
        // ORDER BY since the subquery has no LIMIT, leaving result order undefined.
        // Branching keeps each SQL string fully parameterized via FromSqlInterpolated.
        var tenant = RequireTenant(ctx);
        var approvedState = nameof(ReviewState.Approved);

        if (includeDrafts && includeDeprecated)
            return await db.ExpertiseEntries.FromSqlInterpolated($"""
                SELECT * FROM "ExpertiseEntries"
                WHERE "SearchVector" @@ plainto_tsquery('english', {query})
                  AND ("Tenant" = {tenant} OR "Tenant" = 'shared')
                ORDER BY ts_rank("SearchVector", plainto_tsquery('english', {query})) DESC
                """).ToListAsync(ct);

        if (includeDrafts)
            return await db.ExpertiseEntries.FromSqlInterpolated($"""
                SELECT * FROM "ExpertiseEntries"
                WHERE "SearchVector" @@ plainto_tsquery('english', {query})
                  AND ("Tenant" = {tenant} OR "Tenant" = 'shared')
                  AND "DeprecatedAt" IS NULL
                ORDER BY ts_rank("SearchVector", plainto_tsquery('english', {query})) DESC
                """).ToListAsync(ct);

        if (includeDeprecated)
            return await db.ExpertiseEntries.FromSqlInterpolated($"""
                SELECT * FROM "ExpertiseEntries"
                WHERE "SearchVector" @@ plainto_tsquery('english', {query})
                  AND ("Tenant" = {tenant} OR "Tenant" = 'shared')
                  AND "ReviewState" = {approvedState}
                ORDER BY ts_rank("SearchVector", plainto_tsquery('english', {query})) DESC
                """).ToListAsync(ct);

        return await db.ExpertiseEntries.FromSqlInterpolated($"""
            SELECT * FROM "ExpertiseEntries"
            WHERE "SearchVector" @@ plainto_tsquery('english', {query})
              AND ("Tenant" = {tenant} OR "Tenant" = 'shared')
              AND "ReviewState" = {approvedState}
              AND "DeprecatedAt" IS NULL
            ORDER BY ts_rank("SearchVector", plainto_tsquery('english', {query})) DESC
            """).ToListAsync(ct);
    }

    public async Task<List<ExpertiseEntry>> SemanticSearchAsync(Vector queryVector, TenantContext ctx, int limit, bool includeDrafts, bool includeDeprecated, CancellationToken ct)
    {
        var query = ApplyTenantFilter(db.ExpertiseEntries.AsQueryable(), ctx)
            .Where(e => e.Embedding != null);

        query = ApplyReviewStateFilter(query, includeDrafts);

        if (!includeDeprecated)
            query = query.Where(e => e.DeprecatedAt == null);

        return await query
            .OrderBy(e => e.Embedding!.CosineDistance(queryVector))
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<ExpertiseEntry?> FindExactMatchAsync(string domain, string title, TenantContext ctx, CancellationToken ct)
    {
        return await ApplyTenantFilter(db.ExpertiseEntries.AsQueryable(), ctx)
            .Where(e => e.DeprecatedAt == null)
            .Where(e => e.Domain == domain)
            .Where(e => e.Title.ToLower() == title.ToLower())
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<ExpertiseEntry>> FindExactMatchesAsync(string domain, IReadOnlyList<string> titles, TenantContext ctx, CancellationToken ct)
    {
        var lowerTitles = titles.Select(t => t.ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return await ApplyTenantFilter(db.ExpertiseEntries.AsQueryable(), ctx)
            .Where(e => e.DeprecatedAt == null)
            .Where(e => e.Domain == domain)
            .Where(e => lowerTitles.Contains(e.Title.ToLowerInvariant()))
            .ToListAsync(ct);
    }

    public async Task<ExpertiseEntry?> FindNearestInDomainAsync(string domain, Vector queryVector, double maxDistance, TenantContext ctx, CancellationToken ct)
    {
        var candidate = await ApplyTenantFilter(db.ExpertiseEntries.AsQueryable(), ctx)
            .Where(e => e.DeprecatedAt == null)
            .Where(e => e.Domain == domain)
            .Where(e => e.Embedding != null)
            .OrderBy(e => e.Embedding!.CosineDistance(queryVector))
            .FirstOrDefaultAsync(ct);

        if (candidate?.Embedding is null)
            return null;

        // Threshold check in memory on the single returned vector to avoid double SQL evaluation
        var a = candidate.Embedding.ToArray();
        var b = queryVector.ToArray();
        var distance = CosineDistance(a, b);

        if (distance is null)
        {
            logger.LogWarning(
                "Embedding dimension mismatch in domain {Domain}: stored {StoredDim}, query {QueryDim}. Run 'reembed' to regenerate stored embeddings",
                domain, a.Length, b.Length);
            return null;
        }

        return distance.Value <= maxDistance ? candidate : null;
    }

    public async Task<List<ExpertiseEntry>> FindAllEmbeddingsInDomainAsync(string domain, TenantContext ctx, CancellationToken ct)
    {
        return await ApplyTenantFilter(db.ExpertiseEntries.AsQueryable(), ctx)
            .Where(e => e.DeprecatedAt == null)
            .Where(e => e.Domain == domain)
            .Where(e => e.Embedding != null)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Computes cosine distance between two vectors. Returns null if dimensions differ.
    /// </summary>
    internal static double? CosineDistance(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return null;

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            normA += a[i] * (double)a[i];
            normB += b[i] * (double)b[i];
        }
        return 1.0 - dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
