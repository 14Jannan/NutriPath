namespace NutriPath.Api.Models;

public sealed record WebAuthSettings(string[] AllowedOrigins, bool RequireSecureCookie, bool CrossSiteCookie);
