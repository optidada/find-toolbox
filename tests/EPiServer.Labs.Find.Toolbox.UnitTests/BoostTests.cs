using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static EPiServer.Labs.Find.Toolbox.UnitTests.TestSearch;

namespace EPiServer.Labs.Find.Toolbox.UnitTests
{
    /// <summary>
    /// Default boosts. The values were tuned against a live Find index (see the relevance integration tests):
    /// synonyms must never outrank the user's own words.
    /// </summary>
    [Collection("FindToolboxOptions")]
    public class BoostTests
    {
        private static Dictionary<string, HashSet<string>> Dictionary(params (string phrase, string synonym, bool bidirectional)[] synonyms)
            => SynonymLoader.CreateSynonymDictionary(synonyms.Select(s => TestSynonyms.Create(s.phrase, s.synonym, s.bidirectional)));

        private static Dictionary<string, double> PhraseBoosts(JToken query, string type)
            => query.SelectTokens("$.." + type).SelectMany(t => ((JObject)t).Properties())
                    .ToDictionary(p => (string)p.Value["query"], p => (double)p.Value["boost"]);

        [Fact]
        public void Default_boosts()
        {
            var o = new FindToolboxOptions();
            Assert.Equal(0.1, o.SynonymBoost);
            Assert.Equal(0.2, o.SynonymVariationBoostFactor);
            Assert.Equal(10, o.PhraseBoost);
            Assert.Equal(5, o.PhrasePrefixBoost);
            Assert.Equal(0.5, o.PrefixBoost);
            Assert.Equal(0.4, o.FuzzyBoost);
            Assert.Equal(0.2, o.WildcardBoost);
        }

        [Fact]
        public void Synonym_alternatives_are_down_weighted_and_length_normalized_inside_the_group()
        {
            var result = SynonymExpander.Expand(new[] { "episerver", "find" }, Dictionary(("find", "search & navigation", false), ("find", "fnd", false)));

            // 0.1 for a one term synonym, 0.1 x 1/2 for the two term synonym ("&" is not a term)
            Assert.Equal("episerver (find OR (search AND navigation)^0.05 OR fnd^0.1)", result.ExpandedQuery);
        }

        [Fact]
        public void Shorter_synonym_of_a_phrase_gets_the_full_synonym_boost()
        {
            var result = SynonymExpander.Expand(new[] { "new", "york" }, Dictionary(("new york", "nyc", false)));

            Assert.Equal("((new AND york) OR nyc^0.1)", result.ExpandedQuery);
        }

        [Fact]
        public void Synonym_boost_1_writes_no_boost()
        {
            var result = SynonymExpander.Expand(new[] { "find" }, Dictionary(("find", "search", false)), new FindToolboxOptions { SynonymBoost = 1 });

            Assert.Equal("(find OR search)", result.ExpandedQuery);
        }

        [Fact]
        public void Synonym_boost_is_culture_invariant()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("sv-SE"); // decimal comma
                var result = SynonymExpander.Expand(new[] { "find" }, Dictionary(("find", "search", false)), new FindToolboxOptions { SynonymBoost = 0.25 });

                Assert.Equal("(find OR search^0.25)", result.ExpandedQuery);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Fact]
        public void Synonym_variations_get_a_lower_length_normalized_relevance_boost()
        {
            var service = new UsingSynonymService(new FakeSynonymProvider(("find", "search & navigation", false)), new FindToolboxOptions());

            var json = QueryJson(For("episerver find").UsingSynonymsImproved(service).UsingRelevanceImproved(x => x.Title));
            var phrase = PhraseBoosts(json, "match_phrase");
            var phrasePrefix = PhraseBoosts(json, "match_phrase_prefix");

            Assert.Equal(10, phrase["episerver find"]);
            Assert.Equal(5, phrasePrefix["episerver find"]);

            // 0.2 (variation factor) x 2/3 (original has 2 terms, the variation 3) and "&" removed
            Assert.Equal(10 * 0.2 * 2 / 3, phrase["episerver search navigation"], 6);
            Assert.Equal(5 * 0.2 * 2 / 3, phrasePrefix["episerver search navigation"], 6);
        }

        [Fact]
        public void Single_term_query_gets_a_field_contains_term_boost_and_prefix()
        {
            var json = QueryJson(For("find").UsingRelevanceImproved(x => x.Title));

            Assert.Equal(10, PhraseBoosts(json, "match_phrase")["find"]);
            Assert.NotEmpty(json.SelectTokens("$..prefix"));
        }

        [Fact]
        public void Fuzzy_and_wildcard_default_boosts_come_from_options()
        {
            using (FindToolboxOptions.Override(new FindToolboxOptions { FuzzyBoost = 0.7, WildcardBoost = 0.3 }))
            {
                var json = QueryJson(For("episerver").FuzzyMatch(x => x.Title).WildcardMatch(x => x.Title));
                var queryStrings = QueryStrings(json);

                Assert.Equal(0.7, (double)queryStrings.Single(q => ((string)q["query"]).Contains("~"))["boost"]);
                Assert.Equal(0.3, (double)queryStrings.Single(q => ((string)q["query"]).Contains("*"))["boost"]);
            }
        }
    }
}
