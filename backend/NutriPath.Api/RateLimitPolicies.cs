namespace NutriPath.Api;

/// <summary>Named rate-limit policies, shared by Program.cs and the controllers that use them.</summary>
public static class RateLimitPolicies
{
    public const string Auth = "AuthPolicy";
}
