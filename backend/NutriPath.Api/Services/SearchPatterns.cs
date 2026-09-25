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
    public static string Contains(string text) => $"%{Escape(text)}%";

    /// <summary>A "starts with" pattern, escaped the same way.</summary>
    public static string StartsWith(string text) => $"{Escape(text)}%";

    private static string Escape(string text) => text
        .Replace(@"\", @"\\")
        .Replace("%", @"\%")
        .Replace("_", @"\_");
}
