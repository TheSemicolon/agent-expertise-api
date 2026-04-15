using Microsoft.AspNetCore.Authentication;

namespace ExpertiseApi.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddApiKeyAuth(this IServiceCollection services)
    {
        services.AddAuthentication(ApiKeyAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>(
                ApiKeyAuthHandler.SchemeName, null);

        services.AddAuthorizationBuilder()
            .AddPolicy("ReadAccess", policy =>
                policy.RequireClaim(AuthConstants.ScopeClaimType, AuthConstants.ReadScope))
            .AddPolicy("WriteAccess", policy =>
                policy.RequireClaim(AuthConstants.ScopeClaimType, AuthConstants.WriteScope));

        return services;
    }
}
