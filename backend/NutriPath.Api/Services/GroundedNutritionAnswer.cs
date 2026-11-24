using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using NutriPath.Api.DTOs;
using NutriPath.Api.Models;
using NutriPath.Api.Services.Multilingual;

namespace NutriPath.Api.Services;

/// <summary>
/// Validates the model's JSON answer before anyone sees it: structure, citations (only retrieved,
/// approved sources), quantities (only numbers present in the user's structured data), response
/// language, and allergy-safety guarantees. Anything that fails is replaced by a fixed, localized
/// fallback. These are deterministic checks on top of the prompt rules; they reduce risk but
/// cannot guarantee medical or allergy safety.
/// </summary>
public sealed record GroundedNutritionAnswer(string Answer, List<KnowledgeSource> References, bool InsufficientEvidence)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // Numbers followed by a nutrition unit, in English or the Tamil/Sinhala unit words.
    // (?![A-Za-z]) instead of \b, so "60g" next to a Tamil suffix is still checked.
    private static readonly Regex Quantity = new(
        @"(?<![\w.])(\d+(?:,\d{3})*(?:\.\d+)?)\s*\*{0,2}\s*(?:kcal|calories?|grams?|g|mg|kg|ml|milligrams?|millilitres?|milliliters?" +
        @"|கிலோ\s*கலோரி|கலோரி|கிராம்|மி\.?\s*கி|கி\.?\s*கி|மில்லிகிராம்|மில்லி" +
        @"|කැලරි|කිලෝ\s*කැලරි|මිලි\s*ග්‍?රෑම්|ග්‍?රෑම්|කිලෝ\s*ග්‍?රෑම්|මිලි\s*ලීටර්)(?![A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AllergyGuaranteeEnglish = new(
        @"\b(?:allerg(?:y|en)[\s\p{Pd}]+free|guaranteed\s+safe|completely\s+safe\s+for\s+your\s+allergy|(?:zero|no)\s+(?:risk\s+of\s+)?cross[\s\p{Pd}]+contact|no\s+need\s+to\s+(?:check|verify)\s+(?:the\s+)?ingredients)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Matched against Languages.MatchKey(answer). Reviewed phrases for "allergen-free",
    // "completely safe", "guaranteed safe" and "no need to check".
    private static readonly string[] AllergyGuaranteeNative =
    [
        "ஒவ்வாமை இல்லாத", "ஒவ்வாமையற்ற", "முழுமையாகப் பாதுகாப்பான", "முற்றிலும் பாதுகாப்பான",
        "100% பாதுகாப்பான", "உத்தரவாதமாகப் பாதுகாப்பான", "சரிபார்க்க தேவையில்லை",
        "අසාත්මිකතා රහිත", "සම්පූර්ණයෙන්ම ආරක්ෂිත", "සම්පූර්ණයෙන් ආරක්ෂිත", "100% ආරක්ෂිත",
        "සහතිකවම ආරක්ෂිත", "පරීක්ෂා කිරීමට අවශ්ය නැත",
    ];

    public static string Unverified(string language) => language switch
    {
        Languages.Tamil => "அந்தக் கேள்விக்கு சரிபார்க்கப்பட்ட பதிலை என்னால் வழங்க முடியவில்லை. ஒரு குறிப்பிட்ட உணவு, உணவு வேளை அல்லது ஊட்டச்சத்துத் தலைப்பைப் பற்றிக் கேட்டுப் பாருங்கள்.",
        Languages.Sinhala => "එම ප්‍රශ්නයට තහවුරු කළ පිළිතුරක් දීමට මට නොහැකි විය. නිශ්චිත ආහාරයක්, ආහාර වේලක් හෝ පෝෂණ මාතෘකාවක් ගැන අසා බලන්න.",
        _ => "I couldn't verify a grounded answer for that question. Try asking about a specific food, meal or nutrition topic.",
    };

    public static string UnverifiedAllergySafety(string language) => language switch
    {
        Languages.Tamil => "உங்களுக்குத் தெரிந்த ஒவ்வாமைப் பொருள் உள்ள உணவுகளைத் தவிர்க்கவும். ஒரு மாற்று உணவு பாதுகாப்பானது என்பதை அதன் பட்டியல் விவரத்திலிருந்து என்னால் உறுதிப்படுத்த முடியாது. சாப்பிடுவதற்கு முன், உள்ளடக்கப் பொருட்களையும் குறுக்குத் தொடர்பையும் (cross-contact) உற்பத்தியாளர் அல்லது சமையலறையிடம் சரிபார்க்கவும்.",
        Languages.Sinhala => "ඔබ දන්නා අසාත්මිකතා කාරකය අඩංගු ආහාර වළක්වන්න. විකල්ප ආහාරයක් ආරක්ෂිත බව එහි නාමාවලි විස්තරයෙන් මට තහවුරු කළ නොහැක. ආහාරයට ගැනීමට පෙර අමුද්‍රව්‍ය සහ හරස් ස්පර්ශය (cross-contact) ගැන නිෂ්පාදකයාගෙන් හෝ මුළුතැන්ගෙයින් පරීක්ෂා කරන්න.",
        _ => "Avoid foods that contain your known allergen. I can’t confirm that an alternative is safe from its catalogue entry. Check ingredients and cross-contact with the manufacturer or kitchen before eating.",
    };

    public static GroundedNutritionAnswer Parse(string reply, List<KnowledgeChunk> retrieved, string structuredContext = "{}",
        string language = Languages.English)
    {
        try
        {
            var data = JsonSerializer.Deserialize<ModelAnswer>(reply, Options);
            if (data?.Answer is not { Length: > 0 and <= 3000 } || data.SourceIds is null ||
                data.InsufficientEvidence is null || string.IsNullOrWhiteSpace(data.Answer) ||
                Regex.IsMatch(data.Answer, @"\b(?:https?://|www\.)|\]\(", RegexOptions.IgnoreCase) ||
                HasUnsupportedQuantity(data.Answer, structuredContext) ||
                !IsInLanguage(data.Answer, language))
                return new(Unverified(language), [], true);
            if (GuaranteesAllergySafety(data.Answer))
                return new(UnverifiedAllergySafety(language), [], true);
            var approved = retrieved.Select(c => c.Document)
                .Where(d => d is { Approved: true } && KnowledgeCli.IsSourceUrl(d.SourceUrl))
                .DistinctBy(d => d!.Id).ToDictionary(d => d!.Id);
            if (data.SourceIds.Any(id => !approved.ContainsKey(id)))
                return new(Unverified(language), [], true);
            var references = data.SourceIds.Distinct().Select(id =>
                new KnowledgeSource(id, approved[id]!.Title, approved[id]!.SourceUrl)).ToList();
            return new(data.Answer.Trim(), references, data.InsufficientEvidence.Value);
        }
        catch (JsonException)
        {
            return new(Unverified(language), [], true);
        }
    }

    public static bool GuaranteesAllergySafety(string answer)
    {
        if (AllergyGuaranteeEnglish.IsMatch(answer)) return true;
        var key = Languages.MatchKey(answer);
        return AllergyGuaranteeNative.Any(p => key.Contains(Languages.MatchKey(p), StringComparison.Ordinal));
    }

    /// <summary>
    /// True when the answer is written mainly in the requested script. English food names and
    /// units inside a Tamil or Sinhala answer are fine; a whole answer in the wrong language is not.
    /// </summary>
    public static bool IsInLanguage(string answer, string language)
    {
        var (tamil, sinhala, latin) = Languages.CountScripts(answer);
        var letters = tamil + sinhala + latin;
        if (letters < 8) return true;
        return language switch
        {
            Languages.Tamil => tamil >= 0.3 * letters && sinhala <= 0.05 * letters,
            Languages.Sinhala => sinhala >= 0.3 * letters && tamil <= 0.05 * letters,
            _ => tamil + sinhala <= 0.1 * letters,
        };
    }

    private static bool HasUnsupportedQuantity(string answer, string context)
    {
        static decimal ParseNumber(string value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number : decimal.MinValue;

        using var structured = JsonDocument.Parse(context);
        var values = new HashSet<decimal>();
        CollectNumbers(structured.RootElement, values);
        return Quantity.Matches(answer).Any(match => !values.Contains(ParseNumber(match.Groups[1].Value)));
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
