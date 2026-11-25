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
/// புரதம் / புரதச்சத்து). Romanised (Tanglish/Singlish) entries are compared by
/// <see cref="Transliteration.Key"/>, so common spelling variants match, and entries of five or
/// more letters also match suffixed words ("paruppula", "thanniyai").
/// Extend only with terms reviewed by fluent speakers.
/// </summary>
public static class NutritionTermLexicon
{
    private sealed record Entry(string English, string[] Native, string[] Transliterated);

    private static readonly Entry[] Entries =
    [
        new("protein", ["புரத", "புரோட்டீ", "புரோட்டி", "ப்ரோட்டீ", "ප්‍රෝටී", "ප්‍රොටී", "ප්‍රෝටි"],
            ["puratham", "purotham", "puradham", "purotin", "protin", "proteen", "prothin"]),
        new("carbohydrate carbohydrates carbs", ["மாவுச்சத்", "கார்போஹைட்ரேட்", "கார்போ", "කාබෝහයිඩ්‍රේට්", "කාබෝහයි", "පිෂ්ඨ"],
            ["maavuchathu", "mavuchathu", "carbo", "karbo", "starch"]),
        new("fat fats saturated unsaturated", ["கொழுப்", "මේද"], ["kozhuppu", "koluppu", "kozhuppa", "medaya", "medhaya"]),
        new("oil fat", ["எண்ணெய", "තෙල්"], ["ennai", "ennei", "ennaiy", "thel", "tel"]),
        new("fibre fiber whole foods", ["நார்ச்சத்", "நார்ப்பொருள்", "තන්තු", "කෙඳි"],
            ["naarchathu", "narchathu", "naar", "thanthu", "kendi", "fiber", "fibre"]),
        new("water hydration drinking", ["தண்ணீர", "நீர்ச்சத்", "குடிநீர", "ජලය", "වතුර"],
            ["thanni", "tanni", "thaneer", "thanneer", "kudineer", "wathura", "watura", "vathura", "jalaya", "jalaye"]),
        new("calories calorie energy", ["கலோரி", "ஆற்றல்", "කැලරි", "ශක්ති"], ["kalori", "calori", "kalari", "kaloriyai"]),
        new("weight management weight loss", ["எடை", "உடல் எடை", "සිරුරේ බර", "ශරීර බර", "බර අඩු", "බර වැඩි", "බර කළමනා"],
            ["edai", "udaledai", "bara", "barak"]),
        new("rice curry", ["சோறு", "சோற்", "சாத", "அரிசி", "கறி", "බත්", "බතට", "සහල්", "ව්‍යංජන", "කරි"], ["soru", "sadam", "saadham", "arisi", "bath", "batha", "haal", "kari", "kariya", "kariyak"]),
        new("dhal lentils pulses legumes", ["பருப்", "பயறு", "பயிறு", "கடலை", "පරිප්පු", "මුං", "කඩල", "කව්පි", "රනිල"],
            ["paruppu", "parupu", "parippu", "dhal", "dal", "payaru", "payiru", "kadala", "kadalai", "mung", "mun", "cowpea", "kawpi"]),
        new("vegetables greens", ["காய்கறி", "கீரை", "එළවළු", "කොළ"], ["kaaikari", "kaikari", "kaaykari", "keerai", "elawalu", "elolu", "kola", "kolakenda", "gotukola"]),
        new("fruit", ["பழம்", "பழங்க", "பழச்", "பழத்", "පලතුරු"], ["pazham", "palam", "pazhangal", "palathuru", "palaturu"]),
        new("sugar free sugars", ["சர்க்கரை", "சீனி", "සීනි"], ["sakkarai", "seeni", "sini", "sugar"]),
        new("juice sugary drinks", ["ஜூஸ்", "பழச்சாற", "சாற", "இனிப்பு பான", "ජූස්", "යුෂ", "බීම"], ["juice", "beema", "bima", "saaru", "pazhachaaru"]),
        new("drinks beverages", ["பான", "குளிர்பான", "බීම වර්ග"], ["drinks", "soft", "soda", "coke"]),
        new("drinking water drinks", [], ["kudika", "kudikka", "kudikkanum", "kudikanum", "kudikalama", "bonna", "bonne", "bonnada", "bibila"]),
        new("salt sodium", ["உப்ப", "உப்பு", "ලුණු", "සෝඩියම්"], ["uppu", "lunu", "salt"]),
        new("vegetarian plant-based meat-free", ["சைவ", "நனிசைவ", "නිර්මාංශ", "එළවළු ආහාර"], ["saiva", "saivam", "nirmansha", "nirmaansha", "vegan", "vegetarian", "veg"]),
        new("allergy allergen allergies ingredient labels", ["ஒவ்வாமை", "அலர்ஜி", "අසාත්මික", "ඇලර්ජි"], ["alarji", "allergy", "allergi", "asathmika", "asaathmika"]),
        new("breakfast", ["காலை உணவ", "காலைச் சிற்றுண்டி", "උදේ ආහාර", "උදෑසන ආහාර"], ["kaalai", "kaalaila", "udeta", "udee", "breakfast"]),
        new("lunch", ["மதிய உணவ", "மதிய", "දිවා ආහාර", "දවල්"], ["madhiyam", "mathiyam", "dawal", "dawalta", "lunch"]),
        new("dinner", ["இரவு உணவ", "රාත්‍රී ආහාර", "රෑ කෑම"], ["iravu", "raathiri", "rathiri", "rae", "dinner"]),
        new("meal food", ["உணவ", "சாப்பா", "சாப்பி", "ආහාර", "කෑම"], ["unavu", "saapadu", "sapadu", "saapaadu", "kaema", "kema", "kaama"]),
        new("milk dairy", ["பால்", "කිරි"], ["paal", "kiri", "milk"]),
        new("egg eggs", ["முட்டை", "බිත්තර"], ["muttai", "biththara", "bittara", "egg"]),
        new("fish", ["மீன்", "மீன", "මාළු", "මාලු"], ["meen", "malu", "maalu", "fish"]),
        new("chicken meat", ["கோழி", "இறைச்சி", "කුකුළු", "මස්"], ["kozhi", "koli", "iraichi", "kukul", "kukulmas", "mas", "chicken"]),
        new("coconut", ["தேங்காய", "පොල්"], ["thengai", "thenkai", "pol", "polkiri", "coconut"]),
        new("iron", ["இரும்புச்", "இரும்புச்சத்", "යකඩ"], ["irumbu", "irumbuchathu", "yakada"]),
        new("calcium", ["கால்சிய", "කැල්සියම්"], ["calcium", "kalsiyam"]),
        new("vitamin", ["வைட்டமின்", "விட்டமின்", "විටමින්"], ["vitamin", "vitamins", "vitamine"]),
        new("vitamin b12", ["பி12", "b12"], ["b12"]),
        new("diabetes blood sugar", ["நீரிழிவு", "சர்க்கரை நோய்", "දියවැඩියා"], ["diabetes", "neerizhivu", "diyawadiyawa"]),
        new("exercise physical activity recovery", ["உடற்பயிற்சி", "பயிற்சி", "ව්‍යායාම"], ["udarpayirchi", "vyayama", "vyayamaya", "gym", "workout"]),
        new("supplements", ["சப்ளிமெண்ட்", "துணை உணவு", "අතිරේක"], ["supplement", "supplements"]),
        new("hoppers breakfast staples", ["அப்பம்", "அப்பத்", "ආප්ප"], ["appam", "aappa", "hoppers"]),
        new("string hoppers breakfast staples", ["இடியப்ப", "இடியாப்ப", "ඉඳි ආප්ප", "ඉදි ආප්ප"],
            ["idiyappam", "idiappam", "idiyapam", "indiappa", "indiaappa", "indi"]),
        new("pittu breakfast staples", ["பிட்டு", "පිට්ටු"], ["pittu", "puttu"]),
        new("kottu", ["கொத்து", "කොත්තු"], ["kottu"]),
        new("milk rice kiribath", ["கிரிபத்", "பால் சோறு", "பால் சோற்", "කිරිබත්"], ["kiribath", "kiribat"]),
        new("ingredient labels packaged", ["லேபிள்", "லேபிளி", "ලේබල", "ලේබලය"], ["label", "labels"]),
        new("bone health calcium", ["எலும்ப", "අස්ථි"], ["elumbu", "elumbugal", "bones"]),
        new("balanced", ["சமநிலை", "சமச்சீர", "සමබර", "සමතුලිත"], ["balance", "samanilai", "samabara"]),
        new("portion serving size", ["பரிமாறும் அளவு", "பகுதி அளவு", "කොටස"], ["portion"]),
        new("sambol", ["சம்பல்", "සම්බෝල"], ["sambol", "sambal", "sambola"]),
    ];

    private static readonly (string English, string[] Native, HashSet<string> Transliterated)[] BuiltIn =
        Entries.Select(e => Prepare(e.English, e.Native, e.Transliterated)).ToArray();

    // Built-in entries plus terms approved in the admin portal (replaced atomically on refresh).
    private static volatile (string English, string[] Native, HashSet<string> Transliterated)[] Prepared = BuiltIn;

    private static (string English, string[] Native, HashSet<string> Transliterated) Prepare(
        string english, IEnumerable<string> native, IEnumerable<string> transliterated) =>
        (english,
            native.Select(Languages.MatchKey).Where(n => n.Length > 0).ToArray(),
            transliterated.Select(Transliteration.Key).Where(t => t.Length > 0).ToHashSet());

    /// <summary>Replaces the reviewed terms added in the admin portal. Built-in entries always stay.</summary>
    public static void SetReviewedTerms(IEnumerable<(string English, string[] Native, string[] Transliterated)> terms) =>
        Prepared = [.. BuiltIn, .. terms.Select(t => Prepare(t.English, t.Native, t.Transliterated))];

    public static int BuiltInCount => BuiltIn.Length;

    /// <summary>English search terms for the Tamil, Sinhala or transliterated terms found in the text.</summary>
    public static IReadOnlyList<string> EnglishTermsFor(string text)
    {
        var key = Languages.MatchKey(text);
        var words = System.Text.RegularExpressions.Regex.Split(key, @"[\s\p{P}\p{S}]+")
            .Where(w => w.Length > 0).ToArray();
        var latinWords = words.Where(w => w.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9'))
            .Select(Transliteration.Key).ToHashSet();
        var terms = new List<string>();
        foreach (var (english, native, transliterated) in Prepared)
        {
            var hit = native.Any(n => n.Contains(' ')
                          ? key.Contains(n, StringComparison.Ordinal)
                          : words.Any(w => w.StartsWith(n, StringComparison.Ordinal)))
                      || transliterated.Overlaps(latinWords)
                      || transliterated.Any(t => t.Length >= 5 &&
                             latinWords.Any(w => w.Length > t.Length && w.StartsWith(t, StringComparison.Ordinal)));
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
