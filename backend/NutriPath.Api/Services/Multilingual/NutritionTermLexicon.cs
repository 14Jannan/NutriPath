namespace NutriPath.Api.Services.Multilingual;

/// <summary>
/// Reviewed Tamil, Sinhala and common transliterated nutrition terms mapped to the English
/// vocabulary of the approved knowledge base. The keyword (TF-IDF) fallback only understands
/// English words, so a Tamil or Sinhala question is expanded with these English terms before
/// keyword search. This is a narrow bridge for common nutrition topics, not translation:
/// questions outside these terms get no keyword evidence and fall back to semantic search or
/// an insufficient-evidence answer.
///
/// Native-script entries match as prefixes of words (Tamil and Sinhala add suffixes, e.g.
/// புரதம் / புரதச்சத்து); transliterated entries match whole words.
/// Extend only with terms reviewed by fluent speakers.
/// </summary>
public static class NutritionTermLexicon
{
    private sealed record Entry(string English, string[] Native, string[] Transliterated);

    private static readonly Entry[] Entries =
    [
        new("protein", ["புரத", "புரோட்டீ", "புரோட்டி", "ப்ரோட்டீ", "ප්‍රෝටී", "ප්‍රොටී", "ප්‍රෝටි"],
            ["puratham", "purotin", "protin", "proteen"]),
        new("carbohydrate carbohydrates carbs", ["மாவுச்சத்", "கார்போஹைட்ரேட்", "கார்போ", "කාබෝහයිඩ්‍රේට්", "කාබෝහයි", "පිෂ්ඨ"],
            ["maavuchathu", "mavuchathu", "carbo"]),
        new("fat fats saturated unsaturated", ["கொழுப்", "මේද"], ["kozhuppu", "kozhuppa", "medaya"]),
        new("oil fat", ["எண்ணெய", "තෙල්"], ["ennai", "ennei", "thel"]),
        new("fibre fiber whole foods", ["நார்ச்சத்", "நார்ப்பொருள்", "තන්තු", "කෙඳි"],
            ["naarchathu", "narchathu", "thanthu"]),
        new("water hydration drinking", ["தண்ணீர", "நீர்ச்சத்", "குடிநீர", "ජලය", "වතුර"],
            ["thanni", "thaneer", "thanneer", "wathura", "jalaya"]),
        new("calories calorie energy", ["கலோரி", "ஆற்றல்", "කැලරි", "ශක්ති"], ["kalori", "calori"]),
        new("weight management weight loss", ["எடை", "உடல் எடை", "සිරුරේ බර", "ශරීර බර", "බර අඩු", "බර වැඩි", "බර කළමනා"],
            ["edai", "bara"]),
        new("rice curry", ["சோறு", "சோற்", "சாத", "அரிசி", "கறி", "බත්", "බතට", "සහල්", "ව්‍යංජන", "කරි"], ["soru", "sadam", "saadham", "arisi", "bath", "haal"]),
        new("dhal lentils pulses legumes", ["பருப்", "பயறு", "பயிறு", "கடலை", "පරිප්පු", "මුං", "කඩල", "කව්පි", "රනිල"],
            ["paruppu", "parippu", "payaru", "kadala", "mung", "cowpea"]),
        new("vegetables greens", ["காய்கறி", "கீரை", "එළවළු", "කොළ"], ["kaaikari", "kaikari", "keerai", "elawalu", "kola"]),
        new("fruit", ["பழம்", "பழங்க", "பழச்", "பழத்", "පලතුරු"], ["pazham", "palam", "palathuru"]),
        new("sugar free sugars", ["சர்க்கரை", "சீனி", "සීනි"], ["sakkarai", "seeni", "sini"]),
        new("juice sugary drinks", ["ஜூஸ்", "பழச்சாற", "சாற", "இனிப்பு பான", "ජූස්", "යුෂ", "බීම"], ["juice", "beema"]),
        new("drinks beverages", ["பான", "குளிர்பான", "බීම වර්ග"], ["drinks"]),
        new("salt sodium", ["உப்ப", "உப்பு", "ලුණු", "සෝඩියම්"], ["uppu", "lunu"]),
        new("vegetarian plant-based meat-free", ["சைவ", "நனிசைவ", "නිර්මාංශ", "එළවළු ආහාර"], ["saiva", "saivam", "nirmansha", "vegan"]),
        new("allergy allergen allergies ingredient labels", ["ஒவ்வாமை", "அலர்ஜி", "අසාත්මික", "ඇලර්ජි"], ["alarji", "allergy"]),
        new("breakfast", ["காலை உணவ", "காலைச் சிற்றுண்டி", "උදේ ආහාර", "උදෑසන ආහාර"], ["kaalai", "udeta"]),
        new("lunch", ["மதிய உணவ", "மதிய", "දිවා ආහාර", "දවල්"], ["madhiyam", "dawal", "dawalta"]),
        new("dinner", ["இரவு உணவ", "රාත්‍රී ආහාර", "රෑ කෑම"], ["iravu", "rae"]),
        new("meal food", ["உணவ", "சாப்பா", "சாப்பி", "ආහාර", "කෑම"], ["unavu", "saapadu", "sapadu", "kaema", "kema", "kaama"]),
        new("milk dairy", ["பால்", "කිරි"], ["paal", "kiri"]),
        new("egg eggs", ["முட்டை", "බිත්තර"], ["muttai", "biththara"]),
        new("fish", ["மீன்", "மீன", "මාළු", "මාලු"], ["meen", "malu"]),
        new("chicken meat", ["கோழி", "இறைச்சி", "කුකුළු", "මස්"], ["kozhi", "iraichi", "kukul", "mas"]),
        new("coconut", ["தேங்காய", "පොල්"], ["thengai", "thenkai", "pol"]),
        new("iron", ["இரும்புச்", "இரும்புச்சத்", "යකඩ"], ["irumbu", "yakada"]),
        new("calcium", ["கால்சிய", "කැල්සියම්"], ["calcium", "kalsiyam"]),
        new("vitamin", ["வைட்டமின்", "விட்டமின்", "විටමින්"], ["vitamin", "vitamins", "vitamine"]),
        new("vitamin b12", ["பி12", "b12"], ["b12"]),
        new("diabetes blood sugar", ["நீரிழிவு", "சர்க்கரை நோய்", "දියවැඩියා"], ["diabetes", "neerizhivu"]),
        new("exercise physical activity recovery", ["உடற்பயிற்சி", "பயிற்சி", "ව්‍යායාම"], ["udarpayirchi", "vyayama", "gym"]),
        new("supplements", ["சப்ளிமெண்ட்", "துணை உணவு", "අතිරේක"], ["supplement", "supplements"]),
        new("hoppers breakfast staples", ["அப்பம்", "அப்பத்", "ආප්ප"], ["appam", "aappa", "appa", "hoppers"]),
        new("string hoppers breakfast staples", ["இடியப்ப", "இடியாப்ப", "ඉඳි ආප්ප", "ඉදි ආප්ප"],
            ["idiyappam", "idiappam", "indiappa", "indi"]),
        new("pittu breakfast staples", ["பிட்டு", "පිට්ටු"], ["pittu", "puttu"]),
        new("kottu", ["கொத்து", "කොත්තු"], ["kottu"]),
        new("milk rice kiribath", ["கிரிபத்", "பால் சோறு", "பால் சோற்", "කිරිබත්"], ["kiribath"]),
        new("ingredient labels packaged", ["லேபிள்", "லேபிளி", "ලේබල", "ලේබලය"], ["label", "labels"]),
        new("bone health calcium", ["எலும்ப", "අස්ථි"], ["elumbu"]),
        new("balanced", ["சமநிலை", "சமச்சீர", "සමබර", "සමතුලිත"], ["balance"]),
        new("portion serving size", ["பரிமாறும் அளவு", "பகுதி அளவு", "කොටස"], ["portion"]),
        new("sambol", ["சம்பல்", "සම්බෝල"], ["sambol", "sambal", "sambola"]),
    ];

    private static readonly (string English, string[] Native, HashSet<string> Transliterated)[] Prepared =
        Entries.Select(e => (e.English,
                e.Native.Select(Languages.MatchKey).Where(n => n.Length > 0).ToArray(),
                e.Transliterated.Select(t => t.ToLowerInvariant()).ToHashSet()))
            .ToArray();

    /// <summary>English search terms for the Tamil, Sinhala or transliterated terms found in the text.</summary>
    public static IReadOnlyList<string> EnglishTermsFor(string text)
    {
        var key = Languages.MatchKey(text);
        var words = System.Text.RegularExpressions.Regex.Split(key, @"[\s\p{P}\p{S}]+")
            .Where(w => w.Length > 0).ToArray();
        var latinWords = words.Where(w => w.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9')).ToHashSet();
        var terms = new List<string>();
        foreach (var (english, native, transliterated) in Prepared)
        {
            var hit = native.Any(n => n.Contains(' ')
                          ? key.Contains(n, StringComparison.Ordinal)
                          : words.Any(w => w.StartsWith(n, StringComparison.Ordinal)))
                      || transliterated.Overlaps(latinWords);
            if (hit) terms.Add(english);
        }
        return terms;
    }

    /// <summary>
    /// The query for keyword search: the original text plus mapped English terms when the
    /// question is not plain English. English questions are returned unchanged.
    /// </summary>
    public static string ExpandForKeywordSearch(string question, string language)
    {
        if (language == Languages.English) return question;
        var terms = EnglishTermsFor(question);
        return terms.Count == 0 ? question : question + " " + string.Join(' ', terms);
    }
}
