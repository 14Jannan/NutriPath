using System.Globalization;
using System.Text;

namespace NutriPath.Api.Services.Multilingual;

/// <summary>The assistant's supported languages and Unicode helpers for their scripts.</summary>
public static class Languages
{
    public const string English = "en";
    public const string Tamil = "ta";
    public const string Sinhala = "si";
    public const string Auto = "auto";

    public static readonly IReadOnlyList<string> All = [English, Tamil, Sinhala];

    public static bool IsSupported(string? code) => code is English or Tamil or Sinhala;

    /// <summary>"auto", empty and unknown values mean automatic.</summary>
    public static string? NormalizeCode(string? code)
    {
        var value = code?.Trim().ToLowerInvariant();
        return value switch
        {
            "en" or "english" => English,
            "ta" or "tamil" => Tamil,
            "si" or "sinhala" or "sinhalese" => Sinhala,
            _ => null,
        };
    }

    public static string EnglishName(string code) => code switch
    {
        Tamil => "Tamil",
        Sinhala => "Sinhala",
        _ => "English",
    };

    public static bool IsTamilLetter(char c) => c is >= '஀' and <= '௿';
    public static bool IsSinhalaLetter(char c) => c is >= '඀' and <= '෿';
    public static bool IsLatinLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    /// <summary>
    /// NFC normalization with whitespace collapsed. Zero-width joiners are kept because
    /// Sinhala writes conjuncts such as "ප්‍ර" with U+200D.
    /// </summary>
    public static string Normalize(string text)
    {
        var normalized = (text ?? "").Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        var space = false;
        foreach (var c in normalized)
        {
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>
    /// A comparison key: normalized, lower-case, without zero-width joiners or non-joiners,
    /// so keyboard variants of the same Sinhala or Tamil word compare equal.
    /// </summary>
    public static string MatchKey(string text) =>
        Normalize(text).ToLower(CultureInfo.InvariantCulture).Replace("‍", "").Replace("‌", "");

    /// <summary>Counts Tamil, Sinhala and Latin letters (combining marks count with their script).</summary>
    public static (int Tamil, int Sinhala, int Latin) CountScripts(string text)
    {
        int tamil = 0, sinhala = 0, latin = 0;
        foreach (var c in text)
        {
            if (IsTamilLetter(c)) tamil++;
            else if (IsSinhalaLetter(c)) sinhala++;
            else if (IsLatinLetter(c)) latin++;
        }
        return (tamil, sinhala, latin);
    }
}
