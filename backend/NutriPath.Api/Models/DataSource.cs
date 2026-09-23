namespace NutriPath.Api.Models;

public enum DataSourceType { ExternalApi, ManualSeed }

/// <summary>
/// One row per place nutrition data comes from. Every Food row points
/// back to exactly one of these, so we always know where a number
/// originated and how current it is.
/// </summary>
public class DataSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public DataSourceType Type { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
}