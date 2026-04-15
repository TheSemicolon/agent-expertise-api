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
}
