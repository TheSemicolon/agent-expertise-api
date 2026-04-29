using ExpertiseApi.Auth;
using ExpertiseApi.Models;
using Pgvector;

namespace ExpertiseApi.Data;

/// <summary>
/// Per ADR-001: every method takes a <see cref="TenantContext"/>. Reads are filtered to
/// <c>Tenant IN (ctx.Tenant, "shared")</c>. Writes scope tenant ownership at the repository
/// layer so a caller in tenant A cannot mutate or soft-delete an entry in tenant B
/// (cross-tenant resolves to 404 via <c>FirstOrDefaultAsync</c> returning null).
/// </summary>
public interface IExpertiseRepository
{
    Task<ExpertiseEntry?> GetByIdAsync(Guid id, TenantContext ctx, CancellationToken ct = default);

    Task<List<ExpertiseEntry>> ListAsync(
        TenantContext ctx,
        string? domain = null,
        List<string>? tags = null,
        EntryType? entryType = null,
        Severity? severity = null,
        bool includeDrafts = false,
        bool includeDeprecated = false,
        CancellationToken ct = default);

    Task<ExpertiseEntry> CreateAsync(ExpertiseEntry entry, TenantContext ctx, CancellationToken ct = default);

    Task<ExpertiseEntry?> UpdateAsync(Guid id, TenantContext ctx, Func<ExpertiseEntry, Task> applyUpdates, CancellationToken ct = default);

    Task<bool> SoftDeleteAsync(Guid id, TenantContext ctx, CancellationToken ct = default);

    Task<List<ExpertiseEntry>> KeywordSearchAsync(string query, TenantContext ctx, bool includeDrafts = false, bool includeDeprecated = false, CancellationToken ct = default);

    Task<List<ExpertiseEntry>> SemanticSearchAsync(Vector queryVector, TenantContext ctx, int limit = 10, bool includeDrafts = false, bool includeDeprecated = false, CancellationToken ct = default);

    Task<ExpertiseEntry?> FindExactMatchAsync(string domain, string title, TenantContext ctx, CancellationToken ct = default);

    Task<List<ExpertiseEntry>> FindExactMatchesAsync(string domain, IReadOnlyList<string> titles, TenantContext ctx, CancellationToken ct = default);

    Task<ExpertiseEntry?> FindNearestInDomainAsync(string domain, Vector queryVector, double maxDistance, TenantContext ctx, CancellationToken ct = default);

    Task<List<ExpertiseEntry>> FindAllEmbeddingsInDomainAsync(string domain, TenantContext ctx, CancellationToken ct = default);
}
