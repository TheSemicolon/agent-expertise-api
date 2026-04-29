using ExpertiseApi.Auth;
using ExpertiseApi.Data;
using ExpertiseApi.Models;
using ExpertiseApi.Services;
using Microsoft.AspNetCore.Mvc;
using Pgvector;

namespace ExpertiseApi.Endpoints;

public static class ExpertiseEndpoints
{
    public static RouteGroupBuilder MapExpertiseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/expertise")
            .WithTags("Expertise")
            .RequireAuthorization();

        group.MapGet("/", ListEntries)
            .RequireAuthorization("ReadAccess");

        group.MapGet("/drafts", ListDrafts)
            .RequireAuthorization(AuthConstants.Policies.WriteApproveAccess);

        group.MapGet("/{id:guid}", GetEntry)
            .RequireAuthorization("ReadAccess");

        group.MapPost("/", CreateEntry)
            .RequireAuthorization("WriteAccess");

        group.MapPatch("/{id:guid}", UpdateEntry)
            .RequireAuthorization("WriteAccess");

        group.MapDelete("/{id:guid}", DeleteEntry)
            .RequireAuthorization("WriteAccess");

        group.MapPost("/batch", CreateBatch)
            .RequireAuthorization("WriteAccess");

        group.MapPost("/{id:guid}/approve", ApproveEntry)
            .RequireAuthorization(AuthConstants.Policies.WriteApproveAccess);

        group.MapPost("/{id:guid}/reject", RejectEntry)
            .RequireAuthorization(AuthConstants.Policies.WriteApproveAccess);

        return group;
    }

    private static async Task<IResult> ListEntries(
        HttpContext httpContext,
        IExpertiseRepository repo,
        [FromQuery] string? domain,
        [FromQuery] string? tags,
        [FromQuery] EntryType? entryType,
        [FromQuery] Severity? severity,
        [FromQuery] bool includeDeprecated = false,
        CancellationToken ct = default)
    {
        // Reads always default to ReviewState = Approved. Reviewers see drafts and rejected
        // entries via GET /expertise/drafts (which requires write.approve).
        var tenantContext = httpContext.RequireTenantContext();
        var tagList = tags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var entries = await repo.ListAsync(tenantContext, domain, tagList, entryType, severity, includeDeprecated, ct);
        return Results.Ok(entries);
    }

    private static async Task<IResult> ListDrafts(
        HttpContext httpContext,
        IExpertiseRepository repo,
        CancellationToken ct)
    {
        var tenantContext = httpContext.RequireTenantContext();
        var entries = await repo.ListDraftsAsync(tenantContext, ct);
        return Results.Ok(entries);
    }

    private static async Task<IResult> GetEntry(
        Guid id,
        HttpContext httpContext,
        IExpertiseRepository repo,
        CancellationToken ct)
    {
        var tenantContext = httpContext.RequireTenantContext();
        var entry = await repo.GetByIdAsync(id, tenantContext, ct);
        return entry is null ? Results.NotFound() : Results.Ok(entry);
    }

    private static bool IsRequestValid(CreateExpertiseRequest request) =>
        !string.IsNullOrWhiteSpace(request.Domain) &&
        !string.IsNullOrWhiteSpace(request.Title) &&
        !string.IsNullOrWhiteSpace(request.Body) &&
        !string.IsNullOrWhiteSpace(request.Source);

    private static async Task<IResult> CreateEntry(
        CreateExpertiseRequest request,
        HttpContext httpContext,
        IExpertiseRepository repo,
        EmbeddingService embeddingService,
        DeduplicationService dedup,
        CancellationToken ct)
    {
        if (!IsRequestValid(request))
            return Results.Problem("Domain, Title, Body, and Source are required.", statusCode: 400);

        var tenantContext = httpContext.RequireTenantContext();

        var embedding = await embeddingService.GenerateEmbeddingAsync(
            EmbeddingService.BuildInputText(request.Title, request.Body), ct);

        var (isDuplicate, existing) = await dedup.CheckAsync(request, embedding, tenantContext, ct);
        if (isDuplicate && existing is not null)
            return Results.Conflict(existing);

        var created = await repo.CreateAsync(BuildEntry(request, embedding, tenantContext), tenantContext, ct);
        return Results.Created($"/expertise/{created.Id}", created);
    }

    private static async Task<IResult> UpdateEntry(
        Guid id,
        UpdateExpertiseRequest request,
        HttpContext httpContext,
        IExpertiseRepository repo,
        EmbeddingService embeddingService,
        CancellationToken ct)
    {
        var tenantContext = httpContext.RequireTenantContext();
        var needsReembed = request.Title is not null || request.Body is not null;

        var updated = await repo.UpdateAsync(id, tenantContext, async entry =>
        {
            if (request.Domain is not null) entry.Domain = request.Domain;
            if (request.Tags is not null) entry.Tags = request.Tags;
            if (request.Title is not null) entry.Title = request.Title;
            if (request.Body is not null) entry.Body = request.Body;
            if (request.EntryType is not null) entry.EntryType = request.EntryType.Value;
            if (request.Severity is not null) entry.Severity = request.Severity.Value;
            if (request.Source is not null) entry.Source = request.Source;
            if (request.SourceVersion is not null) entry.SourceVersion = request.SourceVersion;

            if (needsReembed)
            {
                entry.Embedding = await embeddingService.GenerateEmbeddingAsync(
                    EmbeddingService.BuildInputText(entry.Title, entry.Body), ct);
            }
        }, ct);

        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    private static async Task<IResult> CreateBatch(
        List<CreateExpertiseRequest> requests,
        HttpContext httpContext,
        IExpertiseRepository repo,
        EmbeddingService embeddingService,
        DeduplicationService dedup,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        const int MaxBatchSize = 100;
        var tenantContext = httpContext.RequireTenantContext();

        if (requests is null || requests.Count == 0)
            return Results.Problem("Request body must contain at least one entry.", statusCode: 400);

        if (requests.Count > MaxBatchSize)
            return Results.Problem($"Batch size exceeds maximum of {MaxBatchSize} entries.", statusCode: 400);

        var logger = loggerFactory.CreateLogger("ExpertiseApi.Endpoints.BatchIntake");
        var results = new BatchEntryResult[requests.Count];

        // Phase 1: Validate and collect
        var validItems = new List<(int Index, CreateExpertiseRequest Request)>();
        for (var i = 0; i < requests.Count; i++)
        {
            if (!IsRequestValid(requests[i]))
            {
                results[i] = new BatchEntryResult(i, BatchEntryStatus.Rejected, null,
                    "Domain, Title, Body, and Source are required.");
                continue;
            }
            validItems.Add((i, requests[i]));
        }

        if (validItems.Count == 0)
            return Results.Json(results.ToList(), statusCode: 207);

        // Phase 2: Batch embed — single ONNX call for all valid items
        // Phase 3: Batch dedup — bulk queries per domain instead of per item
        IReadOnlyList<Vector> embeddings;
        IReadOnlyList<(bool IsDuplicate, ExpertiseEntry? Existing)> dedupResults;

        try
        {
            var texts = validItems.Select(v => EmbeddingService.BuildInputText(v.Request.Title, v.Request.Body));
            embeddings = await embeddingService.GenerateBatchAsync(texts, ct);
        }
        catch (OperationCanceledException)
        {
            foreach (var (index, _) in validItems)
                results[index] = new BatchEntryResult(index, BatchEntryStatus.Failed, null, "Request was cancelled.");

            return Results.Json(results.ToList(), statusCode: 207);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Batch embedding generation failed");

            foreach (var (index, _) in validItems)
                results[index] = new BatchEntryResult(index, BatchEntryStatus.Failed, null, "Batch could not be processed.");

            return Results.Json(results.ToList(), statusCode: 207);
        }

        try
        {
            var validRequests = validItems.Select(v => v.Request).ToList();
            dedupResults = await dedup.CheckBatchAsync(validRequests, embeddings, tenantContext, ct);
        }
        catch (OperationCanceledException)
        {
            foreach (var (index, _) in validItems)
                results[index] = new BatchEntryResult(index, BatchEntryStatus.Failed, null, "Request was cancelled.");

            return Results.Json(results.ToList(), statusCode: 207);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Batch deduplication failed");

            foreach (var (index, _) in validItems)
                results[index] = new BatchEntryResult(index, BatchEntryStatus.Failed, null, "Batch could not be processed.");

            return Results.Json(results.ToList(), statusCode: 207);
        }

        // Phase 4: Create non-duplicate entries
        for (var j = 0; j < validItems.Count; j++)
        {
            var (index, request) = validItems[j];
            var embedding = embeddings[j];
            var (isDuplicate, existing) = dedupResults[j];

            if (isDuplicate && existing is not null)
            {
                results[index] = new BatchEntryResult(index, BatchEntryStatus.Duplicate, existing.Id, null);
                continue;
            }

            try
            {
                var created = await repo.CreateAsync(BuildEntry(request, embedding, tenantContext), tenantContext, ct);
                results[index] = new BatchEntryResult(index, BatchEntryStatus.Created, created.Id, null);
            }
            catch (OperationCanceledException)
            {
                for (var k = j; k < validItems.Count; k++)
                    results[validItems[k].Index] = new BatchEntryResult(validItems[k].Index, BatchEntryStatus.Failed, null, "Request was cancelled.");
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Batch entry {Index} failed", index);
                results[index] = new BatchEntryResult(index, BatchEntryStatus.Failed, null, "Entry could not be created.");
            }
        }

        var resultList = results.ToList();
        var allCreated = resultList.All(r => r.Status == BatchEntryStatus.Created);
        return allCreated
            ? Results.Ok(resultList)
            : Results.Json(resultList, statusCode: 207);
    }

    private static ExpertiseEntry BuildEntry(
        CreateExpertiseRequest request,
        Vector embedding,
        TenantContext tenantContext) => new()
        {
            Domain = request.Domain,
            Tags = request.Tags ?? [],
            Title = request.Title,
            Body = request.Body,
            EntryType = request.EntryType,
            Severity = request.Severity,
            Source = request.Source,
            SourceVersion = request.SourceVersion,
            Embedding = embedding,
            Tenant = tenantContext.Tenant!,
            AuthorPrincipal = tenantContext.Principal.FindFirst("sub")?.Value
                          ?? tenantContext.Principal.Identity?.Name
                          ?? "unknown",
            AuthorAgent = tenantContext.Agent
        };

    private static async Task<IResult> DeleteEntry(
        Guid id,
        HttpContext httpContext,
        IExpertiseRepository repo,
        CancellationToken ct)
    {
        var tenantContext = httpContext.RequireTenantContext();
        var outcome = await repo.SoftDeleteAsync(id, tenantContext, ct);
        return outcome switch
        {
            WriteOutcome.Success => Results.NoContent(),
            WriteOutcome.NotFound => Results.NotFound(),
            WriteOutcome.InsufficientScope => Results.Problem(
                "Soft-deleting a shared entry requires the expertise.write.approve scope.",
                statusCode: 403),
            _ => Results.Problem("Unexpected outcome from soft-delete.", statusCode: 500)
        };
    }

    private static async Task<IResult> ApproveEntry(
        Guid id,
        HttpContext httpContext,
        IExpertiseRepository repo,
        ApproveExpertiseRequest? request,
        CancellationToken ct)
    {
        var tenantContext = httpContext.RequireTenantContext();
        var visibility = request?.Visibility ?? Visibility.Private;

        var (outcome, entry) = await repo.ApproveAsync(id, tenantContext, visibility, ct);
        return outcome switch
        {
            WriteOutcome.Success => Results.Ok(entry),
            WriteOutcome.NotFound => Results.NotFound(),
            WriteOutcome.InvalidState => Results.Problem(
                "Entry is not in Draft state and cannot be approved.",
                statusCode: 409),
            WriteOutcome.ConcurrentConflict => Results.Problem(
                "Entry was modified concurrently. Retry.",
                statusCode: 409),
            _ => Results.Problem("Unexpected outcome from approve.", statusCode: 500)
        };
    }

    private static async Task<IResult> RejectEntry(
        Guid id,
        RejectExpertiseRequest request,
        HttpContext httpContext,
        IExpertiseRepository repo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RejectionReason))
            return Results.Problem("rejectionReason is required.", statusCode: 400);
        if (request.RejectionReason.Length > MaxRejectionReasonLength)
            return Results.Problem(
                $"rejectionReason exceeds maximum length of {MaxRejectionReasonLength} characters.",
                statusCode: 400);

        var tenantContext = httpContext.RequireTenantContext();
        var (outcome, entry) = await repo.RejectAsync(id, tenantContext, request.RejectionReason, ct);
        return outcome switch
        {
            WriteOutcome.Success => Results.Ok(entry),
            WriteOutcome.NotFound => Results.NotFound(),
            WriteOutcome.InvalidState => Results.Problem(
                "Entry is not in Draft state and cannot be rejected.",
                statusCode: 409),
            WriteOutcome.ConcurrentConflict => Results.Problem(
                "Entry was modified concurrently. Retry.",
                statusCode: 409),
            _ => Results.Problem("Unexpected outcome from reject.", statusCode: 500)
        };
    }

    private const int MaxRejectionReasonLength = 2000;
}

public enum BatchEntryStatus { Created, Duplicate, Rejected, Failed }

public record BatchEntryResult(
    int Index,
    BatchEntryStatus Status,
    Guid? Id,
    string? Error);

public record CreateExpertiseRequest(
    string Domain,
    string Title,
    string Body,
    EntryType EntryType,
    Severity Severity,
    string Source,
    List<string>? Tags = null,
    string? SourceVersion = null);

public record UpdateExpertiseRequest(
    string? Domain = null,
    string? Title = null,
    string? Body = null,
    EntryType? EntryType = null,
    Severity? Severity = null,
    string? Source = null,
    List<string>? Tags = null,
    string? SourceVersion = null);

public record ApproveExpertiseRequest(Visibility? Visibility = null);

public record RejectExpertiseRequest(string RejectionReason);
