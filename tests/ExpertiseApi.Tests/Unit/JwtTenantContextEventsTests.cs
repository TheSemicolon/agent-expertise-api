using ExpertiseApi.Auth;
using FluentAssertions;

namespace ExpertiseApi.Tests.Unit;

public class JwtTenantContextEventsTests
{
    [Fact]
    public void ExpandScopeClosure_NormalizesLegacyWriteToDraft()
    {
        var result = JwtTenantContextEvents.ExpandScopeClosure(new[] { AuthConstants.LegacyWriteScope });

        result.Should().Contain(AuthConstants.WriteDraftScope);
        result.Should().NotContain(AuthConstants.LegacyWriteScope);
    }

    [Fact]
    public void ExpandScopeClosure_AdminImpliesAll()
    {
        var result = JwtTenantContextEvents.ExpandScopeClosure(new[] { AuthConstants.AdminScope });

        result.Should().BeEquivalentTo(new[]
        {
            AuthConstants.AdminScope,
            AuthConstants.WriteApproveScope,
            AuthConstants.WriteDraftScope,
            AuthConstants.ReadScope
        });
    }

    [Fact]
    public void ExpandScopeClosure_ApproveImpliesDraftAndRead()
    {
        var result = JwtTenantContextEvents.ExpandScopeClosure(new[] { AuthConstants.WriteApproveScope });

        result.Should().Contain(AuthConstants.WriteDraftScope);
        result.Should().Contain(AuthConstants.ReadScope);
        result.Should().NotContain(AuthConstants.AdminScope);
    }

    [Fact]
    public void ExpandScopeClosure_DraftImpliesRead()
    {
        var result = JwtTenantContextEvents.ExpandScopeClosure(new[] { AuthConstants.WriteDraftScope });

        result.Should().Contain(AuthConstants.ReadScope);
        result.Should().NotContain(AuthConstants.WriteApproveScope);
    }

    [Fact]
    public void ExpandScopeClosure_ReadStaysRead()
    {
        var result = JwtTenantContextEvents.ExpandScopeClosure(new[] { AuthConstants.ReadScope });

        result.Should().BeEquivalentTo(new[] { AuthConstants.ReadScope });
    }
}
