using ExpertiseApi.Auth;
using ExpertiseApi.Data;
using ExpertiseApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpertiseApi.Endpoints;

internal static class SemanticSearchEndpoints
{
    public static RouteGroupBuilder MapSemanticSearchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/expertise/search/semantic")
            .WithTags("Search")
            .RequireAuthorization("ReadAccess");

        group.MapGet("/", SemanticSearch);

        return group;
    }

    private static async Task<IResult> SemanticSearch(
        HttpContext httpContext,
        IExpertiseRepository repo,
        EmbeddingService embeddingService,
        [FromQuery] string q,
        [FromQuery] int limit = 10,
        [FromQuery] bool includeDeprecated = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.Problem("Query parameter 'q' is required.", statusCode: 400);

        var tenantContext = httpContext.RequireTenantContext();
        var clampedLimit = Math.Clamp(limit, 1, 100);
        var queryVector = await embeddingService.GenerateEmbeddingAsync(q, ct);
        var results = await repo.SemanticSearchAsync(queryVector, tenantContext, clampedLimit, includeDeprecated, ct);
        return Results.Ok(results);
    }
}
