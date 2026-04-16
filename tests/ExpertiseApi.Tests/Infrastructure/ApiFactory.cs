#pragma warning disable SKEXP0070

using ExpertiseApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Pgvector;

namespace ExpertiseApi.Tests.Infrastructure;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public ApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace DbContext with test container connection
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ExpertiseDbContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            services.AddDbContext<ExpertiseDbContext>(options =>
                options.UseNpgsql(_connectionString, o => o.UseVector()));

            // Replace ONNX embedding generator with a mock that returns 384-dim vectors.
            // AddBertOnnxEmbeddingGenerator opens the model file at registration time,
            // so we must remove the existing registration and substitute it.
            var embeddingDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IEmbeddingGenerator<string, Embedding<float>>));
            if (embeddingDescriptor is not null)
                services.Remove(embeddingDescriptor);

            var mockGenerator = Substitute.For<IEmbeddingGenerator<string, Embedding<float>>>();
            mockGenerator.GenerateAsync(
                    Arg.Any<IEnumerable<string>>(),
                    Arg.Any<EmbeddingGenerationOptions?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var inputs = callInfo.ArgAt<IEnumerable<string>>(0).ToList();
                    var embeddings = new List<GeneratedEmbeddings<Embedding<float>>>();
                    var result = new GeneratedEmbeddings<Embedding<float>>();
                    foreach (var _ in inputs)
                    {
                        var vector = new float[384];
                        new Random(42).NextBytes(new byte[4]); // deterministic seed
                        for (var i = 0; i < 384; i++)
                            vector[i] = (float)(new Random(42 + i).NextDouble() * 2 - 1);
                        result.Add(new Embedding<float>(vector));
                    }
                    return Task.FromResult<GeneratedEmbeddings<Embedding<float>>>(result);
                });

            services.AddSingleton(mockGenerator);
        });

        builder.UseSetting("Auth:ApiKey", TestHelpers.TestApiKey);
    }
}
