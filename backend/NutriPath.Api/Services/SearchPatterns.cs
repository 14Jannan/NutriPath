namespace NutriPath.Api.Services;

public static class SearchPatterns
{
    /// <summary>
    /// Builds a "contains" pattern for LIKE/ILIKE with the user's text
    /// escaped, so a typed % or _ is matched literally instead of acting as
    /// a wildcard (Postgres' default escape character is a backslash).
    /// </summary>
    public static string Contains(string text)
    {
        var escaped = text
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");
        return $"%{escaped}%";
    }
}
