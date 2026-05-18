using Microsoft.AspNetCore.Diagnostics;

namespace ExpertiseApi.Diagnostics;

/// <summary>
/// Typed <see cref="IExceptionHandler"/> registered via <c>AddExceptionHandler&lt;T&gt;()</c>
/// — the .NET 8+ preferred shape for global exception logging without claiming the
/// response body (Part D C4).
///
/// Returns <c>false</c> from <see cref="TryHandleAsync"/> so the framework's default
/// <see cref="Microsoft.AspNetCore.Http.IProblemDetailsService"/> writer produces the
/// response, which means the sanitizer registered in <c>AddProblemDetails(...)</c> in
/// <c>Program.cs</c> fires for both <c>Results.Problem(...)</c> and unhandled-exception
/// paths.
///
/// Critically, the full exception (message, type, stack) is logged server-side with
/// the request path here; the customizer then strips Detail/Instance from the wire
/// response in non-Development environments so the correlation ID (traceId) is the
/// only link between the client-visible response and the server-side log entry.
/// </summary>
internal sealed class UnhandledExceptionLogger(ILogger<UnhandledExceptionLogger> log)
    : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(
        HttpContext ctx,
        Exception ex,
        CancellationToken ct)
    {
        log.LogError(ex, "Unhandled exception for {Method} {Path}",
            ctx.Request.Method, ctx.Request.Path);
        // false = let the default IProblemDetailsService writer handle the response body
        // so the AddProblemDetails customizer in Program.cs runs (correlation ID + sanitization).
        return ValueTask.FromResult(false);
    }
}
