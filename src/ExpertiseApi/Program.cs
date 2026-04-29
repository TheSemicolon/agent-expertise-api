#pragma warning disable SKEXP0070

using ExpertiseApi.Auth;
using ExpertiseApi.Cli;
using ExpertiseApi.Data;
using ExpertiseApi.Endpoints;
using ExpertiseApi.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Prometheus;
using Serilog;
using System.Net;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .ReadFrom.Services(services));

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<ExpertiseDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseVector()));

builder.Services.AddScoped<IExpertiseRepository, ExpertiseRepository>();
builder.Services.AddScoped<ITenantContextAccessor, HttpTenantContextAccessor>();
builder.Services.AddExpertiseAuth(builder.Configuration, builder.Environment);

var baseDir = AppContext.BaseDirectory;
var modelPath = builder.Configuration["Onnx:ModelPath"] ?? Path.Combine(baseDir, "models", "model.onnx");
var vocabPath = builder.Configuration["Onnx:VocabPath"] ?? Path.Combine(baseDir, "models", "vocab.txt");

if (File.Exists(modelPath) && File.Exists(vocabPath))
{
    builder.Services.AddBertOnnxEmbeddingGenerator(modelPath, vocabPath);
}
builder.Services.AddScoped<EmbeddingService>();

builder.Services.Configure<DeduplicationOptions>(
    builder.Configuration.GetSection("Deduplication"));
builder.Services.AddScoped<DeduplicationService>();

// X-Forwarded-For support for accurate audit IpAddress capture behind ingress / reverse proxy.
// KnownNetworks must be configured via ForwardedHeaders:KnownNetworks (CIDR list) in
// production — without explicit allowlist the middleware trusts only loopback, which means
// audit IpAddress will record the ingress pod IP rather than the real client.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    var configuredCidrs = builder.Configuration
        .GetSection("ForwardedHeaders:KnownNetworks")
        .Get<string[]>()?
        .Where(static cidr => !string.IsNullOrWhiteSpace(cidr))
        .ToArray();

    // Preserve the framework defaults when no allowlist is configured so only loopback is trusted.
    if (configuredCidrs is null || configuredCidrs.Length == 0)
        return;

    var parsedNetworks = new List<System.Net.IPNetwork>(configuredCidrs.Length);
    foreach (var cidr in configuredCidrs)
    {
        if (!System.Net.IPNetwork.TryParse(cidr, out var network))
            throw new InvalidOperationException(
                $"Invalid ForwardedHeaders:KnownNetworks CIDR entry '{cidr}'.");

        parsedNetworks.Add(network);
    }

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var network in parsedNetworks)
        options.KnownIPNetworks.Add(network);
});

var app = builder.Build();

if (ReembedCommand.IsReembedRequested(args))
{
    await ReembedCommand.RunAsync(app, args);
    return;
}

if (RehashCommand.IsRehashRequested(args))
{
    await RehashCommand.RunAsync(app, args);
    return;
}

// ForwardedHeaders must run before authentication so HttpContext.Connection.RemoteIpAddress
// reflects the real client IP when the audit pipeline reads it.
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseStatusCodePages();
var metricsEnabled = app.Configuration.GetValue<bool>("Metrics:Enabled", true);
if (metricsEnabled)
    app.UseHttpMetrics();
app.UseSerilogRequestLogging();

app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/query", (IWebHostEnvironment env) =>
        Results.File(Path.Combine(env.WebRootPath, "query.html"), "text/html"))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapExpertiseEndpoints();
app.MapSearchEndpoints();
app.MapSemanticSearchEndpoints();
app.MapAuditEndpoints();
if (metricsEnabled)
    app.MapMetrics().AllowAnonymous();

try { app.Run(); }
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally { Log.CloseAndFlush(); }

public partial class Program;
