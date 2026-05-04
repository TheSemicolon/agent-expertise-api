namespace ExpertiseApi.Auth;

internal static class AuthConstants
{
    public const string ScopeClaimType = "scope";

    public const string ReadScope = "expertise.read";
    public const string WriteDraftScope = "expertise.write.draft";
    public const string WriteApproveScope = "expertise.write.approve";
    public const string AdminScope = "expertise.admin";

    /// <summary>
    /// Legacy scope retained for one release cycle so that callers issued tokens before the
    /// scope split still pass <see cref="Policies.WriteAccess"/>. Removed in PR 6 alongside
    /// the production OIDC cutover.
    /// </summary>
    public const string LegacyWriteScope = "expertise.write";

    internal static class Policies
    {
        public const string ReadAccess = "ReadAccess";
        public const string WriteAccess = "WriteAccess";
        public const string WriteApproveAccess = "WriteApproveAccess";
        public const string AdminAccess = "AdminAccess";
    }
}
