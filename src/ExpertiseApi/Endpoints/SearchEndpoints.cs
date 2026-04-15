using ExpertiseApi.Data;
using Microsoft.AspNetCore.Mvc;

namespace ExpertiseApi.Endpoints;

public static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/expertise/search")
            .WithTags("Search")
            .RequireAuthorization("ReadAccess");

        group.MapGet("/", KeywordSearch);

        return group;
    }

    private static async Task<IResult> KeywordSearch(
        IExpertiseRepository repo,
        [FromQuery] string q,
        [FromQuery] bool includeDeprecated = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.Problem("Query parameter 'q' is required.", statusCode: 400);

        var results = await repo.KeywordSearchAsync(q, includeDeprecated, ct);
        return Results.Ok(results);
    }
}
