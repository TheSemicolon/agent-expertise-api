using ExpertiseApi.Models;
using Pgvector;

namespace ExpertiseApi.Data;

public interface IExpertiseRepository
{
    Task<ExpertiseEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<List<ExpertiseEntry>> ListAsync(
        string? domain = null,
        List<string>? tags = null,
        EntryType? entryType = null,
        Severity? severity = null,
        bool includeDeprecated = false,
        CancellationToken ct = default);

    Task<ExpertiseEntry> CreateAsync(ExpertiseEntry entry, CancellationToken ct = default);

    Task<ExpertiseEntry?> UpdateAsync(Guid id, Func<ExpertiseEntry, Task> applyUpdates, CancellationToken ct = default);

    Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default);

    Task<List<ExpertiseEntry>> KeywordSearchAsync(string query, bool includeDeprecated = false, CancellationToken ct = default);

    Task<List<ExpertiseEntry>> SemanticSearchAsync(Vector queryVector, int limit = 10, bool includeDeprecated = false, CancellationToken ct = default);

    Task<ExpertiseEntry?> FindExactMatchAsync(string domain, string title, CancellationToken ct = default);

    Task<ExpertiseEntry?> FindNearestInDomainAsync(string domain, Vector queryVector, double maxDistance, CancellationToken ct = default);
}
