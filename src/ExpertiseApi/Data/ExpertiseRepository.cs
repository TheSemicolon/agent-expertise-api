using ExpertiseApi.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace ExpertiseApi.Data;

public class ExpertiseRepository(ExpertiseDbContext db, ILogger<ExpertiseRepository> logger) : IExpertiseRepository
{
    public async Task<ExpertiseEntry?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.ExpertiseEntries.FindAsync([id], ct);
    }

    public async Task<List<ExpertiseEntry>> ListAsync(
        string? domain,
        List<string>? tags,
        EntryType? entryType,
        Severity? severity,
        bool includeDeprecated,
        CancellationToken ct)
    {
        var query = db.ExpertiseEntries.AsQueryable();

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

    public async Task<ExpertiseEntry> CreateAsync(ExpertiseEntry entry, CancellationToken ct)
    {
        entry.CreatedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;

        db.ExpertiseEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        return entry;
    }

    public async Task<ExpertiseEntry?> UpdateAsync(Guid id, Func<ExpertiseEntry, Task> applyUpdates, CancellationToken ct)
    {
        var entry = await db.ExpertiseEntries.FindAsync([id], ct);
        if (entry is null)
            return null;

        await applyUpdates(entry);
        entry.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct)
    {
        var entry = await db.ExpertiseEntries.FindAsync([id], ct);
        if (entry is null)
            return false;

        entry.DeprecatedAt = DateTime.UtcNow;
        entry.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<ExpertiseEntry>> KeywordSearchAsync(string query, bool includeDeprecated, CancellationToken ct)
    {
        if (includeDeprecated)
        {
            return await db.ExpertiseEntries
                .FromSqlInterpolated($"""
                    SELECT * FROM "ExpertiseEntries"
                    WHERE "SearchVector" @@ plainto_tsquery('english', {query})
                    ORDER BY ts_rank("SearchVector", plainto_tsquery('english', {query})) DESC
                    """)
                .ToListAsync(ct);
        }

        return await db.ExpertiseEntries
            .FromSqlInterpolated($"""
                SELECT * FROM "ExpertiseEntries"
                WHERE "SearchVector" @@ plainto_tsquery('english', {query})
                  AND "DeprecatedAt" IS NULL
                ORDER BY ts_rank("SearchVector", plainto_tsquery('english', {query})) DESC
                """)
            .ToListAsync(ct);
    }

    public async Task<List<ExpertiseEntry>> SemanticSearchAsync(Vector queryVector, int limit, bool includeDeprecated, CancellationToken ct)
    {
        var query = db.ExpertiseEntries
            .Where(e => e.Embedding != null);

        if (!includeDeprecated)
            query = query.Where(e => e.DeprecatedAt == null);

        return await query
            .OrderBy(e => e.Embedding!.CosineDistance(queryVector))
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<ExpertiseEntry?> FindExactMatchAsync(string domain, string title, CancellationToken ct)
    {
        return await db.ExpertiseEntries
            .Where(e => e.DeprecatedAt == null)
            .Where(e => e.Domain == domain)
            .Where(e => e.Title.ToLower() == title.ToLower())
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ExpertiseEntry?> FindNearestInDomainAsync(string domain, Vector queryVector, double maxDistance, CancellationToken ct)
    {
        var candidate = await db.ExpertiseEntries
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
