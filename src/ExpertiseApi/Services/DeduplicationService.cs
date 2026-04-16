using ExpertiseApi.Data;
using ExpertiseApi.Endpoints;
using ExpertiseApi.Models;
using Microsoft.Extensions.Options;
using Pgvector;

namespace ExpertiseApi.Services;

public class DeduplicationOptions
{
    public bool Enabled { get; set; } = true;
    public double SemanticThreshold { get; set; } = 0.10;
}

public class DeduplicationService(IExpertiseRepository repo, IOptions<DeduplicationOptions> options)
{
    public async Task<(bool IsDuplicate, ExpertiseEntry? Existing)> CheckAsync(
        CreateExpertiseRequest request, Vector embedding, CancellationToken ct = default)
    {
        var opts = options.Value;

        if (!opts.Enabled)
            return (false, null);

        var exact = await repo.FindExactMatchAsync(request.Domain, request.Title, ct);
        if (exact is not null && exact.Body == request.Body)
            return (true, exact);

        var nearest = await repo.FindNearestInDomainAsync(request.Domain, embedding, opts.SemanticThreshold, ct);
        if (nearest is not null)
            return (true, nearest);

        return (false, null);
    }

    public async Task<IReadOnlyList<(bool IsDuplicate, ExpertiseEntry? Existing)>> CheckBatchAsync(
        IReadOnlyList<CreateExpertiseRequest> requests,
        IReadOnlyList<Vector> embeddings,
        CancellationToken ct = default)
    {
        var opts = options.Value;
        var results = new (bool IsDuplicate, ExpertiseEntry? Existing)[requests.Count];

        if (!opts.Enabled)
            return results;

        // Group by domain for bulk queries
        var domainGroups = requests
            .Select((r, i) => (Request: r, Index: i, Embedding: embeddings[i]))
            .GroupBy(x => x.Request.Domain);

        foreach (var group in domainGroups)
        {
            var domain = group.Key;
            var items = group.ToList();

            // Bulk exact-match: one query per domain instead of N
            var titles = items.Select(x => x.Request.Title).ToList();
            var exactMatches = await repo.FindExactMatchesAsync(domain, titles, ct);
            var matchByTitle = exactMatches
                .GroupBy(e => e.Title.ToLower())
                .ToDictionary(g => g.Key, g => g.First());

            // Bulk semantic: fetch all domain embeddings once, match in memory
            List<ExpertiseEntry>? domainEntries = null;

            foreach (var item in items)
            {
                // Check exact match
                if (matchByTitle.TryGetValue(item.Request.Title.ToLower(), out var exact)
                    && exact.Body == item.Request.Body)
                {
                    results[item.Index] = (true, exact);
                    continue;
                }

                // Check semantic match in memory
                domainEntries ??= await repo.FindAllEmbeddingsInDomainAsync(domain, ct);

                ExpertiseEntry? nearest = null;
                double nearestDistance = double.MaxValue;

                foreach (var entry in domainEntries)
                {
                    var a = entry.Embedding!.ToArray();
                    var b = item.Embedding.ToArray();
                    var distance = ExpertiseRepository.CosineDistance(a, b);

                    if (distance is not null && distance.Value <= opts.SemanticThreshold && distance.Value < nearestDistance)
                    {
                        nearest = entry;
                        nearestDistance = distance.Value;
                    }
                }

                if (nearest is not null)
                {
                    results[item.Index] = (true, nearest);
                    continue;
                }

                results[item.Index] = (false, null);
            }
        }

        return results;
    }
}
