namespace ExpertiseApi.Auth;

public enum AuthMode
{
    /// <summary>OIDC only. Required for non-Development environments.</summary>
    Oidc,

    /// <summary>Custom dev token format <c>Bearer dev-{tenant}-{scope1}+{scope2}</c>. Development only.</summary>
    LocalDev,

    /// <summary>Legacy static API key. Development only.</summary>
    ApiKey,

    /// <summary>Accepts API key, JWT, and LocalDev tokens. Development default.</summary>
    Hybrid
}
