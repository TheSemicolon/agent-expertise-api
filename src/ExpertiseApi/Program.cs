#pragma warning disable SKEXP0070

using ExpertiseApi.Auth;
using ExpertiseApi.Cli;
using ExpertiseApi.Data;
using ExpertiseApi.Endpoints;
using ExpertiseApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddBertOnnxEmbeddingGenerator(modelPath, vocabPath);
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

app.UseExceptionHandler();
app.UseStatusCodePages();

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

app.Run();
