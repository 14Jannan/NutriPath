namespace NutriPath.Api.DTOs;

public record ScoreComponent(string Key, string Label, string Status, int Percent, string Note, string Tone);

public record WeeklyScoreResponse(int Overall, List<int> DailyScores, List<ScoreComponent> Components);
