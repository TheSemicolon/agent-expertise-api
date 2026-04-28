#pragma warning disable SKEXP0070

using ExpertiseApi.Auth;
using ExpertiseApi.Cli;
using ExpertiseApi.Data;
using ExpertiseApi.Endpoints;
using ExpertiseApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Prometheus;
using Serilog;
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
builder.Services.AddApiKeyAuth();

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

app.UseExceptionHandler();
app.UseStatusCodePages();
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
app.MapMetrics().AllowAnonymous();

try { app.Run(); }
catch (Exception ex) { Log.Fatal(ex, "Application terminated unexpectedly"); }
finally { Log.CloseAndFlush(); }

public partial class Program;
