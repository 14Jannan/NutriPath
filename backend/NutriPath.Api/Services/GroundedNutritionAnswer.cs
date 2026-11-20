using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services;

public sealed record GroundedNutritionAnswer(string Answer, List<KnowledgeSource> References, bool InsufficientEvidence)
{
    private const string Unverified = "I couldn't verify a grounded answer for that question. Try asking about a specific food, meal or nutrition topic.";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static GroundedNutritionAnswer Parse(string reply, List<KnowledgeChunk> retrieved, string structuredContext = "{}")
    {
        try
        {
            var data = JsonSerializer.Deserialize<ModelAnswer>(reply, Options);
            if (data?.Answer is not { Length: > 0 and <= 3000 } || data.SourceIds is null ||
                data.InsufficientEvidence is null || string.IsNullOrWhiteSpace(data.Answer) ||
                Regex.IsMatch(data.Answer, @"\b(?:https?://|www\.)|\]\(", RegexOptions.IgnoreCase) ||
                HasUnsupportedQuantity(data.Answer, structuredContext))
                return new(Unverified, [], true);
            var approved = retrieved.Select(c => c.Document)
                .Where(d => d is { Approved: true } && KnowledgeCli.IsSourceUrl(d.SourceUrl))
                .DistinctBy(d => d!.Id).ToDictionary(d => d!.Id);
            if (data.SourceIds.Any(id => !approved.ContainsKey(id)))
                return new(Unverified, [], true);
            var references = data.SourceIds.Distinct().Select(id =>
                new KnowledgeSource(id, approved[id]!.Title, approved[id]!.SourceUrl)).ToList();
            return new(data.Answer.Trim(), references, data.InsufficientEvidence.Value);
        }
        catch (JsonException)
        {
            return new(Unverified, [], true);
        }
    }

    private static bool HasUnsupportedQuantity(string answer, string context)
    {
        static decimal ParseNumber(string value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number : decimal.MinValue;

        using var structured = JsonDocument.Parse(context);
        var values = new HashSet<decimal>();
        CollectNumbers(structured.RootElement, values);
        return Regex.Matches(answer, @"(?<![\w.])(\d+(?:,\d{3})*(?:\.\d+)?)\s*\*{0,2}\s*(?:kcal|calories?|grams?|g|mg|kg|milligrams?)\b",
                RegexOptions.IgnoreCase)
            .Any(match => !values.Contains(ParseNumber(match.Groups[1].Value)));
    }

    private static void CollectNumbers(JsonElement element, HashSet<decimal> values)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
            values.Add(number);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CollectNumbers(item, values);
        else if (element.ValueKind == JsonValueKind.Object)
            foreach (var item in element.EnumerateObject()) CollectNumbers(item.Value, values);
    }

    private sealed record ModelAnswer(string? Answer, Guid[]? SourceIds, bool? InsufficientEvidence);
}
