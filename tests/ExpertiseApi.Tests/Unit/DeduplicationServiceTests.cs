using ExpertiseApi.Data;
using ExpertiseApi.Endpoints;
using ExpertiseApi.Models;
using ExpertiseApi.Services;
using ExpertiseApi.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pgvector;

namespace ExpertiseApi.Tests.Unit;

public class DeduplicationServiceTests
{
    private readonly IExpertiseRepository _repo = Substitute.For<IExpertiseRepository>();
    private readonly Vector _testVector = TestHelpers.CreateTestVector();

    private DeduplicationService CreateService(bool enabled = true, double threshold = 0.10)
    {
        var options = Options.Create(new DeduplicationOptions
        {
            Enabled = enabled,
            SemanticThreshold = threshold
        });
        return new DeduplicationService(_repo, options);
    }

    private static CreateExpertiseRequest CreateRequest(
        string domain = "shared",
        string title = "Test",
        string body = "Test body") =>
        new(domain, title, body, EntryType.Pattern, Severity.Info, "test");

    [Fact]
    public async Task CheckAsync_WhenDisabled_ReturnsNotDuplicate()
    {
        var service = CreateService(enabled: false);
        var request = CreateRequest();

        var (isDuplicate, existing) = await service.CheckAsync(request, _testVector);

        isDuplicate.Should().BeFalse();
        existing.Should().BeNull();
        await _repo.DidNotReceive().FindExactMatchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAsync_WhenExactMatchWithSameBody_ReturnsDuplicate()
    {
        var service = CreateService();
        var request = CreateRequest(body: "Exact body");
        var existingEntry = TestHelpers.SeedEntry(body: "Exact body");

        _repo.FindExactMatchAsync("shared", "Test", Arg.Any<CancellationToken>())
            .Returns(existingEntry);

        var (isDuplicate, existing) = await service.CheckAsync(request, _testVector);

        isDuplicate.Should().BeTrue();
        existing.Should().Be(existingEntry);
    }

    [Fact]
    public async Task CheckAsync_WhenExactMatchWithDifferentBody_FallsToSemanticCheck()
    {
        var service = CreateService();
        var request = CreateRequest(body: "New body");
        var existingEntry = TestHelpers.SeedEntry(body: "Different body");

        _repo.FindExactMatchAsync("shared", "Test", Arg.Any<CancellationToken>())
            .Returns(existingEntry);
        _repo.FindNearestInDomainAsync("shared", _testVector, 0.10, Arg.Any<CancellationToken>())
            .Returns((ExpertiseEntry?)null);

        var (isDuplicate, _) = await service.CheckAsync(request, _testVector);

        isDuplicate.Should().BeFalse();
        await _repo.Received(1).FindNearestInDomainAsync("shared", _testVector, 0.10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckAsync_WhenSemanticMatchBelowThreshold_ReturnsDuplicate()
    {
        var service = CreateService();
        var request = CreateRequest();
        var nearEntry = TestHelpers.SeedEntry(title: "Similar entry");

        _repo.FindExactMatchAsync("shared", "Test", Arg.Any<CancellationToken>())
            .Returns((ExpertiseEntry?)null);
        _repo.FindNearestInDomainAsync("shared", _testVector, 0.10, Arg.Any<CancellationToken>())
            .Returns(nearEntry);

        var (isDuplicate, existing) = await service.CheckAsync(request, _testVector);

        isDuplicate.Should().BeTrue();
        existing.Should().Be(nearEntry);
    }

    [Fact]
    public async Task CheckAsync_WhenNoMatch_ReturnsNotDuplicate()
    {
        var service = CreateService();
        var request = CreateRequest();

        _repo.FindExactMatchAsync("shared", "Test", Arg.Any<CancellationToken>())
            .Returns((ExpertiseEntry?)null);
        _repo.FindNearestInDomainAsync("shared", _testVector, 0.10, Arg.Any<CancellationToken>())
            .Returns((ExpertiseEntry?)null);

        var (isDuplicate, existing) = await service.CheckAsync(request, _testVector);

        isDuplicate.Should().BeFalse();
        existing.Should().BeNull();
    }
}
