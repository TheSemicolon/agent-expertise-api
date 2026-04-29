using ExpertiseApi.Auth;
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
        HttpContext httpContext,
        IExpertiseRepository repo,
        [FromQuery] string q,
        [FromQuery] bool includeDrafts = false,
        [FromQuery] bool includeDeprecated = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.Problem("Query parameter 'q' is required.", statusCode: 400);

        var tenantContext = httpContext.RequireTenantContext();

        if (includeDrafts && !tenantContext.Scopes.Contains(AuthConstants.WriteApproveScope))
            return Results.Problem(
                "?includeDrafts=true requires the expertise.write.approve scope.",
                statusCode: 403);

        var results = await repo.KeywordSearchAsync(q, tenantContext, includeDrafts, includeDeprecated, ct);
        return Results.Ok(results);
    }
}
