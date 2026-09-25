namespace NutriPath.Api.Models;

/// <summary>
/// The fixed set of avatars a user can pick. Only these IDs are ever stored,
/// so a client can't save arbitrary text as an "avatar". The app draws each
/// one (icon and colour) from the same IDs in
/// frontend/NutriPath.Mobile/src/constants/avatars.ts: keep the two lists in sync.
/// </summary>
public static class AvatarCatalog
{
    public static readonly IReadOnlyList<string> Ids = new[]
    {
        // Fruit and vegetables
        "apple", "carrot", "watermelon", "pineapple", "cherries", "grapes", "citrus", "corn",
        // Animals
        "cat", "dog", "panda", "penguin", "owl", "rabbit", "turtle", "koala", "elephant", "bee", "butterfly", "duck",
        // Active and nature
        "leaf", "sprout", "runner", "dumbbell",
    };

    private static readonly HashSet<string> IdSet = new(Ids, StringComparer.Ordinal);

    public static bool IsValid(string? id) => id != null && IdSet.Contains(id);
}
