using FluentAssertions;

namespace ExpertiseApi.Tests.Unit;

public class CosineDistanceTests
{
    private static double ComputeCosineDistance(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            normA += a[i] * (double)a[i];
            normB += b[i] * (double)b[i];
        }
        return 1.0 - dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    [Fact]
    public void IdenticalVectors_ShouldHaveZeroDistance()
    {
        var v = new float[] { 1.0f, 0.0f, 0.0f };
        ComputeCosineDistance(v, v).Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void OrthogonalVectors_ShouldHaveDistanceOne()
    {
        var a = new float[] { 1.0f, 0.0f };
        var b = new float[] { 0.0f, 1.0f };
        ComputeCosineDistance(a, b).Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void OppositeVectors_ShouldHaveDistanceTwo()
    {
        var a = new float[] { 1.0f, 0.0f };
        var b = new float[] { -1.0f, 0.0f };
        ComputeCosineDistance(a, b).Should().BeApproximately(2.0, 1e-10);
    }

    [Fact]
    public void SimilarVectors_ShouldHaveSmallDistance()
    {
        var a = new float[] { 1.0f, 0.1f, 0.0f };
        var b = new float[] { 1.0f, 0.2f, 0.0f };
        var distance = ComputeCosineDistance(a, b);
        distance.Should().BeGreaterThan(0.0);
        distance.Should().BeLessThan(0.05);
    }

    [Fact]
    public void ZeroVector_ShouldProduceNaN()
    {
        var a = new float[] { 1.0f, 0.0f };
        var b = new float[] { 0.0f, 0.0f };
        var distance = ComputeCosineDistance(a, b);
        double.IsNaN(distance).Should().BeTrue();
    }
}
