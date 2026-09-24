namespace NutriPath.Api.DTOs;

public record DataSourceStatus(string Name, DateTime? LastSyncedAtUtc, int? DaysSinceSync, int FoodCount);
