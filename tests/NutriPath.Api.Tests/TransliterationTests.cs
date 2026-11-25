using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NutriPath.Api.Data;
using NutriPath.Api.Services;
using NutriPath.Api.Services.Multilingual;

namespace NutriPath.Api.Tests;

/// <summary>Tamil and Sinhala typed in English letters (Tanglish, Singlish).</summary>
public class TransliterationTests
{
    [Theory]
    [InlineData("thanni", "tanni")]
    [InlineData("kozhuppu", "koluppu")]
    [InlineData("wathura", "vathura")]
    [InlineData("venum", "vennum")]
    [InlineData("dhinamum", "dinamum")]
    [InlineData("Saapadu", "sapadu")]
    public void SpellingVariantsShareAKey(string a, string b) =>
        Assert.Equal(Transliteration.Key(a), Transliteration.Key(b));

    [Fact]
    public void TanglishAndSinglishMarkersDoNotOverlapEachOtherOrCommonEnglish()
    {
        Assert.Empty(Transliteration.TanglishMarkers.Intersect(Transliteration.SinglishMarkers));
        var english = Transliteration.KeySet(Transliteration.EnglishWords);
        Assert.Empty(Transliteration.TanglishMarkers.Intersect(english));
        Assert.Empty(Transliteration.SinglishMarkers.Intersect(english));
    }

    [Theory]
    [InlineData("enakku protein evlo venum?")]
    [InlineData("thanni evlo kudikkanum dhinamum?")]
    [InlineData("tanni evlo kudikanum?")]
    [InlineData("paruppula irumbuchathu nalla kedaikkuma?")]
    [InlineData("gym ku apram protein venuma?")]
    [InlineData("peanut allergy irundha label la enna paakanum?")]
    [InlineData("idiyappam kooda enna saapidalam?")]
    [InlineData("uppu evlo kammi pannanum?")]
    public void DetectsTanglish(string question) =>
        Assert.Equal((Languages.Tamil, false), LanguageResolver.Detect(question));

    [Theory]
    [InlineData("mata protein kochchara oneda?")]
    [InlineData("wathura kochchara bonna oneda?")]
    [InlineData("vathura kochara bonna oneda?")]
    [InlineData("lunu adu karanne kohomada?")]
    [InlineData("gym ekata passe protein ona da?")]
    [InlineData("allergy thiyenawa nam label eke mokada balanna one?")]
    [InlineData("bath ekka mokada kanna honda?")]
    [InlineData("parippu walin yakada hariyata ganna puluwanda?")]
    public void DetectsSinglish(string question) =>
        Assert.Equal((Languages.Sinhala, false), LanguageResolver.Detect(question));

    [Fact]
    public void EnglishBenchmarkQuestionsAreNeverTakenForTanglishOrSinglish()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "evaluation", "nutrition-questions.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var questions = json.RootElement.EnumerateArray().Select(q => q.GetProperty("question").GetString()!).ToList();
        Assert.True(questions.Count >= 100);
        Assert.All(questions, q => Assert.NotEqual(Languages.Tamil, LanguageResolver.Detect(q).Language));
        Assert.All(questions, q => Assert.NotEqual(Languages.Sinhala, LanguageResolver.Detect(q).Language));
    }

    [Fact]
    public void RomanisedQuestionsGetAScriptReplyByDefault()
    {
        var resolver = new LanguageResolver();
        Assert.Equal(new LanguageResolution("ta", "detected"), resolver.Resolve("enakku protein evlo venum?", "auto", null));
        Assert.Equal(new LanguageResolution("si", "detected"), resolver.Resolve("mata protein kochchara oneda?", "auto", null));
        // Replies must be in Tamil or Sinhala script; a romanised reply fails the language check.
        Assert.False(GroundedNutritionAnswer.IsInLanguage("Unga daily protein target 113 g, paruppu nalla source.", "ta"));
        Assert.True(GroundedNutritionAnswer.IsInLanguage("உங்கள் தினசரி புரத இலக்கு 113 g.", "ta"));
    }

    [Theory]
    [InlineData("tanni evlo kudikanum", "water")]
    [InlineData("koluppu ulla saapadu", "fat")]
    [InlineData("vathura bonna oneda", "water")]
    [InlineData("paruppula irumbu irukka", "dhal")]
    [InlineData("paruppula irumbu irukka", "iron")]
    [InlineData("bittara kanna honda", "egg")]
    [InlineData("idiyapam kooda enna", "string")]
    public void RomanisedVariantsMapToEnglishTerms(string question, string expected) =>
        Assert.Contains(NutritionTermLexicon.EnglishTermsFor(question), t => t.Split(' ').Contains(expected));

    [Theory]
    [InlineData("tanni evlo kudikanum dinamum?", "Drinking water and hydration")]
    [InlineData("uppu evlo kammi pannanum?", "Salt and sodium")]
    [InlineData("kaalaila idiyappam kooda enna saapidalam?", "Sri Lankan breakfast staples")]
    [InlineData("vathura kochara bonna oneda?", "Drinking water and hydration")]
    [InlineData("lunu adu karanne kohomada?", "Salt and sodium")]
    [InlineData("parippu walin yakada ganna puluwanda?", "Iron and absorption")]
    public async Task KeywordFallbackFindsEvidenceForTanglishAndSinglish(string question, string expectedTitle)
    {
        await using var fixture = await MultilingualRetrievalTests.ReviewedKnowledge.CreateAsync();
        var results = await fixture.Retrieval.SearchWithModeAsync(question, "keyword", evaluationTopK: 3);
        Assert.Contains(expectedTitle, results.Select(c => c.Document!.Title));
    }

    [Fact]
    public async Task OnlyRomanisedQuestionsAreExpandedForSemanticSearch()
    {
        await using var fixture = await MultilingualRetrievalTests.ReviewedKnowledge.CreateAsync();
        var retrieval = fixture.Retrieval;
        var expanded = retrieval.SemanticQuery("enakku puratham evlo venum?", Languages.Tamil);
        Assert.StartsWith("enakku puratham evlo venum? (", expanded);
        Assert.Contains("protein", expanded);
        Assert.Equal("புரதம் எவ்வளவு தேவை?", retrieval.SemanticQuery("புரதம் எவ்வளவு தேவை?", Languages.Tamil));
        Assert.Equal("How much protein?", retrieval.SemanticQuery("How much protein?", Languages.English));
        Assert.Equal("enakku evlo venum?", retrieval.SemanticQuery("enakku evlo venum?", Languages.Tamil)); // nothing mapped

        var name = Guid.NewGuid().ToString();
        await using var services = new ServiceCollection()
            .AddDbContext<NutriPathDbContext>(o => o.UseInMemoryDatabase(name)).BuildServiceProvider();
        var disabled = new HybridKnowledgeRetrieval(
            new KnowledgeRetrievalService(services.GetRequiredService<IServiceScopeFactory>()),
            services.GetRequiredService<NutriPathDbContext>(), new NoEmbeddings(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Knowledge:TransliterationQueryExpansion"] = "false",
            }).Build(),
            NullLogger<HybridKnowledgeRetrieval>.Instance);
        Assert.Equal("enakku puratham evlo venum?", disabled.SemanticQuery("enakku puratham evlo venum?", Languages.Tamil));
    }

    private sealed class NoEmbeddings : IEmbeddingService
    {
        public string Model => EmbeddingModels.MultilingualE5Small;
        public bool Enabled => false;
        public Task<float[][]> EmbedAsync(string[] texts, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }
}
