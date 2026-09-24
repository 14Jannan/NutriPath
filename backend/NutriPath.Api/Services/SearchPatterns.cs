namespace NutriPath.Api.Services;

public static class SearchPatterns
{
    // Passed explicitly to ILike so the escaping never depends on the
    // database's default escape character.
    public const string EscapeCharacter = @"\";

    /// <summary>
    /// Builds a "contains" pattern for LIKE/ILIKE with the user's text
    /// escaped, so a typed % or _ is matched literally instead of acting as
    /// a wildcard. Use with <see cref="EscapeCharacter"/>.
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
