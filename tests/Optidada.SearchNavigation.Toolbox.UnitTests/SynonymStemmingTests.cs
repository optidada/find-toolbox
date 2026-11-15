using System.Collections.Generic;
using System.Linq;
using EPiServer.Find;
using Optidada.SearchNavigation.Toolbox.Queries;
using Optidada.SearchNavigation.Toolbox.Synonyms;
using Xunit;
using static Optidada.SearchNavigation.Toolbox.UnitTests.TestSearch;

namespace Optidada.SearchNavigation.Toolbox.UnitTests
{
    /// <summary>Synonyms are also looked up by word stem (Snowball, the search's language).</summary>
    [Collection("ToolboxOptions")]
    public class SynonymStemmingTests
    {
        private static Dictionary<string, HashSet<string>> Dictionary(params (string phrase, string synonym, bool bidirectional)[] synonyms)
            => FindSynonymProvider.CreateSynonymDictionary(synonyms.Select(s => TestSynonyms.Create(s.phrase, s.synonym, s.bidirectional)));

        private static SynonymExpansion Expand(string query, Language language, params (string, string, bool)[] synonyms)
            => SynonymExpander.Expand(QueryText.GetQueryPhrases(query), Dictionary(synonyms), TestSearch.Unboosted,
                SynonymStemming.For(language), language?.FieldSuffix);

        [Theory]
        [InlineData("notebooks", "(notebooks OR laptop)")]
        [InlineData("laptops", "(laptops OR notebook)")]
        [InlineData("notebook", "(notebook OR laptop)")]   // exact match still works
        public void English_plurals_find_the_synonyms(string query, string expected)
        {
            Assert.Equal(expected, Expand(query, Language.English, ("laptop", "notebook", true)).ExpandedQuery);
        }

        [Theory]
        [InlineData("koppar", "(koppar OR mugg)")]
        [InlineData("kopparna", "(kopparna OR mugg)")]
        [InlineData("datorer", "(datorer OR pc)")]       // Snowball: datorer => dator => dat, dator => dat
        [InlineData("dator", "(dator OR pc)")]
        public void Swedish_inflections_find_the_synonyms(string query, string expected)
        {
            Assert.Equal(expected, Expand(query, Language.Swedish, ("kopp", "mugg", false), ("dator", "pc", false)).ExpandedQuery);
        }

        [Fact]
        public void Multi_word_phrases_are_stemmed_per_word()
        {
            var result = Expand("cheap mobile phones", Language.English, ("mobile phone", "smartphone", false));

            Assert.Equal("cheap ((mobile AND phones) OR smartphone)", result.ExpandedQuery);
        }

        [Fact]
        public void Without_a_language_there_is_no_stemming()
        {
            Assert.False(Expand("notebooks", null, ("laptop", "notebook", true)).HasSynonyms);
            Assert.Null(SynonymStemming.For(Language.None));
        }

        [Fact]
        public void The_search_language_picks_the_stemmer()
        {
            var service = new SynonymService(new FakeSynonymProvider(("laptop", "notebook", true)), Unboosted);

            var english = QueryStrings(QueryJson(TestSearch.Client.Search<Doc>(Language.English).For("notebooks").UsingSynonymsImproved(service))).First();
            var noLanguage = QueryStrings(QueryJson(For("notebooks").UsingSynonymsImproved(service))).First();

            Assert.Equal("(notebooks OR laptop)", (string)english["query"]);
            Assert.Equal("notebooks", (string)noLanguage["query"]);
        }

        [Fact]
        public void Stemming_can_be_switched_off()
        {
            var options = new ToolboxOptions { StemSynonymLookup = false, SynonymBoost = 1 };
            var service = new SynonymService(new FakeSynonymProvider(("laptop", "notebook", true)), options);

            var query = QueryStrings(QueryJson(TestSearch.Client.Search<Doc>(Language.English).For("notebooks").UsingSynonymsImproved(service))).First();

            Assert.Equal("notebooks", (string)query["query"]);
        }
    }
}
