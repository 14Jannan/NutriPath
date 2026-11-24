using System.Text;
using System.Text.RegularExpressions;

namespace NutriPath.Api.Services.Multilingual;

/// <summary>
/// Helpers for Tamil and Sinhala typed in English letters ("Tanglish", "Singlish").
///
/// Romanisation has no standard spelling: the same word appears as "thanni"/"tanni",
/// "kozhuppu"/"koluppu", "wathura"/"vathura", "venum"/"vennum". <see cref="Key"/> folds these
/// common variants onto one comparison key; every marker and lexicon entry is compared by key.
/// </summary>
public static class Transliteration
{
    private static readonly Regex Word = new("[a-z0-9]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Lower-case spelling key: zh→l, th→t, dh→d, ph→f, w→v, then runs of a letter collapsed.</summary>
    public static string Key(string word)
    {
        var lower = word.ToLowerInvariant()
            .Replace("zh", "l").Replace("th", "t").Replace("dh", "d").Replace("ph", "f").Replace('w', 'v');
        var builder = new StringBuilder(lower.Length);
        foreach (var c in lower)
            if (builder.Length == 0 || builder[^1] != c) builder.Append(c);
        return builder.ToString();
    }

    /// <summary>The spelling keys of the Latin-script words in the text.</summary>
    public static IReadOnlyList<string> Keys(string text) =>
        Word.Matches(Languages.Normalize(text).ToLowerInvariant()).Select(m => Key(m.Value)).ToList();

    /// <summary>True when the text has Latin letters but no Tamil or Sinhala script.</summary>
    public static bool IsRomanised(string text)
    {
        var (tamil, sinhala, latin) = Languages.CountScripts(text);
        return latin > 0 && tamil + sinhala == 0;
    }

    public static HashSet<string> KeySet(IEnumerable<string> words) => words.Select(Key).ToHashSet();

    // Common Tanglish words: pronouns, question words, verbs and everyday food words.
    // Reviewed for overlap with Singlish and frequent English words (see tests).
    public static readonly HashSet<string> TanglishMarkers = KeySet(
    [
        "enakku", "enaku", "ennaku", "evvalavu", "evalavu", "evlo", "evalo", "evlavu", "evloo", "venum", "vennum",
        "venuma", "vendum", "vendama", "venam", "venaam", "enna", "ennaa", "eppadi", "epdi", "yeppadi", "sollunga",
        "sollu", "solunga", "saapadu", "sapadu", "saappadu", "sapaadu", "sapidalama", "saapidalama", "saapidalaam",
        "sapidanum", "saapidanum", "saapidunga", "sapten", "kudikkanum", "kudikanum", "kudikalama", "kudikka",
        "thanni", "thaneer", "thanneer", "tanni", "illa", "illai", "irukku", "iruku", "irukka", "irukkuma",
        "romba", "konjam", "nallathu", "nalladhu", "nalla", "puratham", "purotham", "kozhuppu", "koluppu",
        "paruppu", "parupu", "soru", "sadam", "saadham", "arisi", "kaalai", "kaalaila", "madhiyam", "mathiyam",
        "iravu", "raathiri", "unavu", "unavula", "edai", "kammi", "kamiya", "jasthi", "jaasthi", "athigam",
        "adhigam", "kuraikka", "kuraikanum", "kuraiyanum", "ungal", "unga", "ungaluku", "ennoda", "enoda", "naan",
        "dhinamum", "thinamum", "vazhaipazham", "muttai", "meen", "kozhi", "koli", "thengai", "keerai", "kaaikari",
        "kaikari", "pazham", "sakkarai", "uppu", "paal", "idiyappam", "idiyapam", "appam", "puttu", "thayir",
        "thosai", "dosai", "idli", "vadai", "chappathi", "dhaan", "baakki", "baaki", "innaikku", "inniku",
        "naalaikku", "nethu", "saaptu", "saapta", "pathi", "nallatha", "kedaikkum", "kidaikkum", "edhu", "ethu",
    ]);

    public static readonly HashSet<string> SinglishMarkers = KeySet(
    [
        "mata", "mage", "mama", "oya", "oyata", "oyage", "mokada", "mokakda", "monawada", "monada", "kohomada",
        "kohoma", "kiyada", "kiyanna", "kiyala", "kanna", "kanne", "kaewa", "kawa", "kaawe", "kanawa", "kanavada",
        "oneda", "onada", "ona", "oney", "puluwanda", "puluwan", "ganna", "gannada", "keeyada", "kochchara",
        "kochcharada", "hari", "neda", "karanna", "karanne", "karannada", "bath", "wathura", "watura", "vathura",
        "bonna", "bonne", "bonnada", "thiyenawa", "thiyanawa", "thiyena", "ehema", "honda", "hondada", "naraka",
        "kema", "kaema", "kaama", "udeta", "udee", "dawal", "dawalta", "rae", "bara", "wadi", "wadiya", "parippu",
        "elawalu", "elolu", "palathuru", "sini", "seeni", "lunu", "kiri", "biththara", "bittara", "malu", "maalu",
        "kukul", "kukulmas", "mas", "dinapatha", "dawasata", "dawase", "aththa", "nadda", "nedda", "eka", "ekak",
        "ekka", "walata", "wage", "hinda", "nisa", "kiyak", "monawa", "mokak", "keeyak", "thiyenne", "aduwen",
    ]);

    // Grammatical particles written as separate words ("gym ku", "label la", "protein ah"; "ona da",
    // "gym ekata", "allergy nam"). Very common, and not English words.
    private static readonly HashSet<string> TanglishParticles = KeySet(["ku", "kku", "la", "ah", "nu", "dhan", "thane", "thaana"]);
    private static readonly HashSet<string> SinglishParticles = KeySet(["da", "eka", "ekak", "ekata", "eke", "ekai", "nam", "walata", "walin", "saha", "godak", "naha", "nehe"]);

    // Typical verb endings: Tamil "-anum" (must), "-alama" (may I), "-kkuma"/"-uma" (will it),
    // "-unga" (please), "-radhu"; Sinhala "-nawa" (present), "-nna" (to ...), "-nnada", "-wada".
    private static readonly string[] TanglishEndings = ["anum", "alama", "alaam", "kuma", "unga", "radu", "ratu", "rada"];
    private static readonly string[] SinglishEndings = ["nava", "nna", "nnada", "vada", "nnam", "gena", "lada"];

    /// <summary>
    /// How strongly a word (already a <see cref="Key"/>) points to Tanglish or Singlish: a known
    /// word, a known stem with a suffix ("paruppula", "kedaikkuma"), a particle, or a verb ending.
    /// </summary>
    public static (int Tamil, int Sinhala) Score(string key)
    {
        static bool Known(HashSet<string> markers, string word) =>
            markers.Contains(word) ||
            (word.Length >= 6 && markers.Any(m => m.Length >= 5 && word.StartsWith(m, StringComparison.Ordinal)));
        static bool Ends(string[] endings, string word) =>
            word.Length >= 5 && endings.Any(e => word.EndsWith(e, StringComparison.Ordinal));

        var tamil = Known(TanglishMarkers, key) || TanglishParticles.Contains(key) || Ends(TanglishEndings, key);
        var sinhala = Known(SinglishMarkers, key) || SinglishParticles.Contains(key) || Ends(SinglishEndings, key);
        return (tamil ? 1 : 0, sinhala ? 1 : 0);
    }

    // Frequent English words: a Latin-script message full of these is English.
    public static readonly HashSet<string> EnglishWords =
    [
        "the", "is", "are", "what", "how", "much", "many", "do", "does", "did", "can", "should", "i", "my", "me",
        "need", "eat", "food", "foods", "drink", "with", "for", "and", "or", "a", "an", "of", "to", "in", "on",
        "which", "why", "when", "today", "daily", "good", "best", "healthy", "about", "your", "you", "it", "this",
        "that", "be", "have", "has", "give", "tell", "show", "help", "please", "per", "day", "meal", "diet",
        "is", "was", "will", "would", "could", "there", "some", "any", "more", "less", "than", "after", "before",
    ];
}
