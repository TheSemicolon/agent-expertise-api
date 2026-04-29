using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExpertiseApi.Auth;
using ExpertiseApi.Data;
using ExpertiseApi.Models;
using ExpertiseApi.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertiseApi.Tests.Integration;

/// <summary>
/// Cross-tenant isolation tests. Each scenario seeds entries in two different tenants
/// (and one in <c>shared</c>) then asserts the caller in tenant <c>test</c> sees only
/// their own and the shared rows. These guard the failure mode PR 3 was built to close:
/// a caller in tenant A reading or mutating an entry in tenant B.
/// </summary>
[Collection("Postgres")]
public class TenantIsolationTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private JwtApiFactory _factory = null!;
    private HttpClient _testClient = null!;

    public TenantIsolationTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _factory = new JwtApiFactory(_postgres.ConnectionString);

        // Caller is tenant "test" with read + draft scopes (no approve scope, no admin).
        var token = JwtTokenMinter.Mint(
            tenant: "test",
            scopes: [AuthConstants.ReadScope, AuthConstants.WriteDraftScope],
            groups: ["group-test"]);
        _testClient = _factory.CreateClient();
        _testClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpertiseDbContext>();
        await db.ExpertiseEntries.IgnoreQueryFilters()
            .ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        _testClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetById_CrossTenantEntry_Returns404()
    {
        var other = await SeedEntry(tenant: "other-team");

        var response = await _testClient.GetAsync($"/expertise/{other.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_OnlyReturnsCallerTenantAndShared()
    {
        var ownEntry = await SeedEntry(tenant: "test", title: "own-entry");
        var sharedEntry = await SeedEntry(tenant: "shared", title: "shared-entry");
        var otherEntry = await SeedEntry(tenant: "other-team", title: "other-entry");

        var response = await _testClient.GetAsync("/expertise");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadJsonElementAsync();
        json.GetArrayLength().Should().Be(2);
        var titles = new[] { json[0].GetProperty("title").GetString(), json[1].GetProperty("title").GetString() };
        titles.Should().Contain("own-entry");
        titles.Should().Contain("shared-entry");
        titles.Should().NotContain("other-entry");
    }

    [Fact]
    public async Task List_DefaultExcludesDrafts()
    {
        await SeedEntry(tenant: "test", title: "approved", reviewState: ReviewState.Approved);
        await SeedEntry(tenant: "test", title: "draft", reviewState: ReviewState.Draft);

        var response = await _testClient.GetAsync("/expertise");

        var json = await response.Content.ReadJsonElementAsync();
        json.GetArrayLength().Should().Be(1);
        json[0].GetProperty("title").GetString().Should().Be("approved");
    }

    [Fact]
    public async Task List_IncludeDraftsWithoutApproveScope_Returns403()
    {
        // _testClient carries read + draft scopes but NOT approve.
        await SeedEntry(tenant: "test", reviewState: ReviewState.Draft);

        var response = await _testClient.GetAsync("/expertise?includeDrafts=true");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_IncludeDraftsWithApproveScope_ReturnsDrafts()
    {
        var approved = await SeedEntry(tenant: "test", title: "approved", reviewState: ReviewState.Approved);
        var draft = await SeedEntry(tenant: "test", title: "draft", reviewState: ReviewState.Draft);

        var approveToken = JwtTokenMinter.Mint(
            tenant: "test",
            scopes: [AuthConstants.ReadScope, AuthConstants.WriteApproveScope],
            groups: ["group-test"]);
        using var approveClient = _factory.CreateClient();
        approveClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", approveToken);

        var response = await approveClient.GetAsync("/expertise?includeDrafts=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadJsonElementAsync();
        json.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task KeywordSearch_CrossTenantEntry_NotIncluded()
    {
        // Same matchable text in two tenants — only the caller's match should be returned.
        await SeedEntry(tenant: "test", title: "Database migration guide for own tenant",
            body: "Database migration best practices for our team.");
        await SeedEntry(tenant: "other-team", title: "Database migration guide for other team",
            body: "Database migration practices for the other team.");

        var response = await _testClient.GetAsync("/expertise/search?q=migration");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadJsonElementAsync();
        json.GetArrayLength().Should().Be(1);
        json[0].GetProperty("tenant").GetString().Should().Be("test");
    }

    [Fact]
    public async Task SemanticSearch_CrossTenantEntry_NotIncluded()
    {
        await SeedEntry(tenant: "test", title: "own");
        await SeedEntry(tenant: "other-team", title: "other");

        var response = await _testClient.GetAsync("/expertise/search/semantic?q=test&limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadJsonElementAsync();
        for (var i = 0; i < json.GetArrayLength(); i++)
            json[i].GetProperty("tenant").GetString().Should().NotBe("other-team");
    }

    [Fact]
    public async Task Update_CrossTenantEntry_Returns404()
    {
        var other = await SeedEntry(tenant: "other-team");

        var payload = new { title = "hijacked" };
        var response = await _testClient.PatchAsJsonAsync($"/expertise/{other.Id}", payload);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_CrossTenantEntry_Returns404()
    {
        var other = await SeedEntry(tenant: "other-team");

        var response = await _testClient.DeleteAsync($"/expertise/{other.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_TitleCollidesWithCrossTenantEntry_CreatesAsNonDuplicate()
    {
        // The dedup leak: before PR 3, a POST in tenant A whose title collided with a
        // tenant B entry would 409 with the B entry's full body. Now: dedup is tenant-
        // scoped, so the collision is invisible and the POST succeeds.
        await SeedEntry(tenant: "other-team", domain: "shared",
            title: "Cross-tenant collision title", body: "Other team's secret body");

        var payload = new
        {
            domain = "shared",
            title = "Cross-tenant collision title",
            body = "Test team's body",
            entryType = "Pattern",
            severity = "Info",
            source = "test"
        };
        var response = await _testClient.PostAsJsonAsync("/expertise", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<ExpertiseEntry> SeedEntry(
        string tenant,
        string domain = "shared",
        string title = "isolation-seed",
        string body = "isolation seed body for keyword search indexing",
        ReviewState reviewState = ReviewState.Approved)
    {
        // Seed directly via DbContext + IgnoreQueryFilters so we can plant rows in any
        // tenant without going through the per-request tenant accessor.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpertiseDbContext>();
        var entry = TestHelpers.SeedEntry(
            domain: domain, title: title, body: body, tenant: tenant, reviewState: reviewState);
        db.ExpertiseEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }
}
