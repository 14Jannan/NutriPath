using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NutriPath.Api.Models;

namespace NutriPath.Api.Services.Multilingual;

/// <param name="Code">"en", "ta" or "si".</param>
/// <param name="Source">
/// Why this language was chosen: "explicit" (asked for in the message), "selected" (the chat
/// language picker), "detected" (script or transliteration markers), "preference" (saved
/// preference), "default" or "disabled" (multilingual mode off).
/// </param>
/// <param name="Ambiguous">True when the question alone didn't identify a language.</param>
public sealed record LanguageResolution(string Code, string Source, bool Ambiguous = false);

public interface ILanguageResolver
{
    LanguageResolution Resolve(string question, string? selectedLanguage, string? savedPreference);
}

/// <summary>
/// Chooses the response language: explicit request in the message, then the selected chat
/// language, then the detected language, then the saved preference, then the default.
///
/// Detection is deliberately lightweight. Tamil and Sinhala scripts are identified reliably
/// from Unicode ranges. Transliterated Tamil ("Tanglish") and Sinhala ("Singlish") are only
/// recognised from a small list of common words, which cannot be reliable on its own: short
/// or unclear Latin-script messages are treated as ambiguous and fall back to the saved
/// preference or the default instead of guessing.
/// </summary>
public sealed class LanguageResolver(IOptions<AiSettings> settings) : ILanguageResolver
{
    public LanguageResolver() : this(Options.Create(new AiSettings())) { }

    private static readonly Regex EnglishExplicit = new(
        @"\b(?:answer|reply|respond|explain|write|say|tell(?:\s+me)?|translate|speak|talk)\b[^.?!\n]{0,40}?\b(?:in|into|using)\s+(?<lang>english|tamil|sinhala|sinhalese)\b" +
        @"|\b(?:in|into)\s+(?<lang2>english|tamil|sinhala|sinhalese)(?:\s+please)?\s*[.!?]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Matched against Languages.MatchKey output (lower-case, joiners removed).
    private static readonly (string Phrase, string Language)[] NativeExplicit =
    [
        ("தமிழில்", Languages.Tamil), ("ஆங்கிலத்தில்", Languages.English), ("சிங்களத்தில்", Languages.Sinhala),
        ("සිංහලෙන්", Languages.Sinhala), (Languages.MatchKey("ඉංග්‍රීසියෙන්"), Languages.English),
        ("දෙමළෙන්", Languages.Tamil), ("දෙමලෙන්", Languages.Tamil),
    ];

    private static readonly Regex TransliteratedExplicit = new(
        @"\b(?:tamil\s*-?\s*la|thamizh\s*-?\s*la|tamizh\s*-?\s*la)\b(?<ta>)" +
        @"|\b(?:sinhalen|sinhalayen|singhalen)\b(?<si>)" +
        @"|\b(?:english\s*-?\s*la|englishla|ingreesiyen|englishen)\b(?<en>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LatinWord = new("[a-z]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public LanguageResolution Resolve(string question, string? selectedLanguage, string? savedPreference)
    {
        var options = settings.Value.Multilingual;
        if (!options.Enabled) return new(Languages.English, "disabled");
        var supported = options.SupportedLanguages
            .Select(Languages.NormalizeCode).OfType<string>().ToHashSet();
        bool Allowed(string? code) => code is not null && supported.Contains(code);

        // 1. An explicit request in the message ("answer in Tamil", "සිංහලෙන් කියන්න").
        if (ExplicitRequest(question) is { } requested && Allowed(requested))
            return new(requested, "explicit");

        // 2. The language selected in the chat.
        var selected = Languages.NormalizeCode(selectedLanguage);
        if (Allowed(selected)) return new(selected!, "selected");

        // 3. Detection.
        var (detected, ambiguous) = Detect(question);
        if (detected is not null && !ambiguous && Allowed(detected)) return new(detected, "detected");

        // 4. Saved preference, 5. default.
        var saved = Languages.NormalizeCode(savedPreference);
        if (Allowed(saved)) return new(saved!, "preference", Ambiguous: true);
        var fallback = Languages.NormalizeCode(options.DefaultLanguage);
        return new(Allowed(fallback) ? fallback! : Languages.English, "default", Ambiguous: true);
    }

    public static string? ExplicitRequest(string question)
    {
        var key = Languages.MatchKey(question);
        foreach (var (phrase, language) in NativeExplicit)
            if (key.Contains(phrase, StringComparison.Ordinal)) return language;

        var english = EnglishExplicit.Match(question);
        if (english.Success)
        {
            var name = english.Groups["lang"].Success ? english.Groups["lang"].Value : english.Groups["lang2"].Value;
            if (!Regex.IsMatch(question, @"\btamil\s+nadu\b", RegexOptions.IgnoreCase))
                return Languages.NormalizeCode(name);
        }

        var transliterated = TransliteratedExplicit.Match(question);
        if (transliterated.Success)
        {
            if (transliterated.Groups["ta"].Success) return Languages.Tamil;
            if (transliterated.Groups["si"].Success) return Languages.Sinhala;
            if (transliterated.Groups["en"].Success) return Languages.English;
        }
        return null;
    }

    /// <summary>Returns the detected language and whether the evidence was too weak to rely on.</summary>
    public static (string? Language, bool Ambiguous) Detect(string question)
    {
        var text = Languages.Normalize(question);
        var (tamil, sinhala, _) = Languages.CountScripts(text);
        if (tamil + sinhala >= 2)
            return (tamil >= sinhala ? Languages.Tamil : Languages.Sinhala, false);

        var words = LatinWord.Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();
        if (words.Count == 0) return (null, true);

        // Spelling keys fold romanisation variants ("thanni"/"tanni", "wathura"/"vathura").
        // A word counts for a language if it is a known word, a known stem with a suffix, a
        // grammatical particle or has a typical verb ending (see Transliteration.Score).
        var scores = words.Where(w => !Transliteration.EnglishWords.Contains(w))
            .Select(w => Transliteration.Score(Transliteration.Key(w))).ToList();
        var ta = scores.Sum(s => s.Tamil);
        var si = scores.Sum(s => s.Sinhala);
        var en = words.Count(Transliteration.EnglishWords.Contains);
        if (Math.Max(ta, si) >= 2 && ta != si && Math.Max(ta, si) >= en)
            return (ta > si ? Languages.Tamil : Languages.Sinhala, false);
        if (Math.Max(ta, si) >= 1 && words.Count <= 4)
            return (ta > si ? Languages.Tamil : ta < si ? Languages.Sinhala : null, true);
        // Short Latin messages ("ok", "and lunch?") are follow-ups whose language is unclear.
        if (en >= 1 && words.Count >= 3) return (Languages.English, false);
        return (Languages.English, true);
    }
}
