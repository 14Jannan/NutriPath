namespace NutriPath.Api.Services;

public record ComponentScore(string Name, int Percent);
public record WeeklyScoreResult(int Overall, List<ComponentScore> Components);

public interface IWeeklyScoreService
{
    Task<WeeklyScoreResult> CalculateAsync(Guid userId);
}
