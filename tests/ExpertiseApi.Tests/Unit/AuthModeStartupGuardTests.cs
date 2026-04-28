using ExpertiseApi.Auth;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

namespace ExpertiseApi.Tests.Unit;

public class AuthModeStartupGuardTests
{
    [Theory]
    [InlineData("Production", AuthMode.ApiKey)]
    [InlineData("Production", AuthMode.LocalDev)]
    [InlineData("Production", AuthMode.Hybrid)]
    [InlineData("Staging", AuthMode.ApiKey)]
    [InlineData("Staging", AuthMode.Hybrid)]
    public void EnforceModeGuard_NonOidcOutsideDevelopment_Throws(string env, AuthMode mode)
    {
        var environment = new HostingEnvironment { EnvironmentName = env };

        var act = () => AuthExtensions.EnforceModeGuard(mode, environment);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage($"*Auth:Mode '{mode}'*Development*");
    }

    [Theory]
    [InlineData(AuthMode.Oidc)]
    [InlineData(AuthMode.LocalDev)]
    [InlineData(AuthMode.ApiKey)]
    [InlineData(AuthMode.Hybrid)]
    public void EnforceModeGuard_AnyMode_IsPermittedInDevelopment(AuthMode mode)
    {
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        var act = () => AuthExtensions.EnforceModeGuard(mode, environment);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void EnforceModeGuard_OidcMode_BootsInAnyEnvironment(string env)
    {
        var environment = new HostingEnvironment { EnvironmentName = env };

        var act = () => AuthExtensions.EnforceModeGuard(AuthMode.Oidc, environment);

        act.Should().NotThrow();
    }

    [Fact]
    public void ParseAuthMode_DefaultsToHybridInDevelopment()
    {
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        var mode = AuthExtensions.ParseAuthMode(null, environment);

        mode.Should().Be(AuthMode.Hybrid);
    }

    [Fact]
    public void ParseAuthMode_DefaultsToOidcOutsideDevelopment()
    {
        var environment = new HostingEnvironment { EnvironmentName = "Production" };

        var mode = AuthExtensions.ParseAuthMode(null, environment);

        mode.Should().Be(AuthMode.Oidc);
    }

    [Theory]
    [InlineData("oidc", AuthMode.Oidc)]
    [InlineData("OIDC", AuthMode.Oidc)]
    [InlineData("Hybrid", AuthMode.Hybrid)]
    [InlineData("apikey", AuthMode.ApiKey)]
    [InlineData("LocalDev", AuthMode.LocalDev)]
    public void ParseAuthMode_IsCaseInsensitive(string input, AuthMode expected)
    {
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        var mode = AuthExtensions.ParseAuthMode(input, environment);

        mode.Should().Be(expected);
    }

    [Fact]
    public void ParseAuthMode_RejectsUnknownValues()
    {
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        var act = () => AuthExtensions.ParseAuthMode("garbage", environment);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*not a recognized mode*");
    }

    private class HostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
