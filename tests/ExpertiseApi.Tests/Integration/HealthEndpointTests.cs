using System.Net;
using System.Net.Http.Json;
using ExpertiseApi.Tests.Infrastructure;
using FluentAssertions;

namespace ExpertiseApi.Tests.Integration;

[Collection("Postgres")]
public class HealthEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private ApiFactory _factory = null!;

    public HealthEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_postgres.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Health_ReturnsHealthyStatus()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadJsonAsync<Dictionary<string, string>>();
        body.Should().ContainKey("status");
        body!["status"].Should().Be("healthy");
    }
}
