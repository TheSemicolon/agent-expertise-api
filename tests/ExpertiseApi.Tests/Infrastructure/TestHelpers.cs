using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExpertiseApi.Models;
using Pgvector;

namespace ExpertiseApi.Tests.Infrastructure;

public static class TestHelpers
{
    public const string TestApiKey = "test-api-key-12345";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<T?> ReadJsonAsync<T>(this HttpContent content)
        => await content.ReadFromJsonAsync<T>(JsonOptions);

    public static async Task<JsonElement> ReadJsonElementAsync(this HttpContent content)
    {
        var stream = await content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    public static HttpClient CreateAuthenticatedClient(ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestApiKey);
        return client;
    }

    public static HttpClient CreateUnauthenticatedClient(ApiFactory factory)
        => factory.CreateClient();

    public static ExpertiseEntry SeedEntry(
        string domain = "shared",
        string title = "Test entry",
        string body = "Test body content for search indexing",
        EntryType entryType = EntryType.Pattern,
        Severity severity = Severity.Info,
        string source = "test")
    {
        return new ExpertiseEntry
        {
            Domain = domain,
            Title = title,
            Body = body,
            EntryType = entryType,
            Severity = severity,
            Source = source,
            Tags = ["test"],
            Embedding = CreateTestVector()
        };
    }

    public static Vector CreateTestVector(int dimensions = 384)
    {
        var values = new float[dimensions];
        var rng = new Random(42);
        for (var i = 0; i < dimensions; i++)
            values[i] = (float)(rng.NextDouble() * 2 - 1);
        return new Vector(values);
    }
}
