using EPiServer.Find;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Linq;
using System.Threading;
using Xunit;
using static EPiServer.Labs.Find.Toolbox.UnitTests.TestSearch;

namespace EPiServer.Labs.Find.Toolbox.UnitTests
{
    /// <summary>
    /// All toolbox extensions work on the same main query, so they can be combined in any order after For().
    /// </summary>
    [Collection("FindToolboxOptions")]
    public class CallOrderTests
    {
        private static readonly UsingSynonymService FindSynonym = Synonyms(("find", "search & navigation", false));

        private static string[] Fields(JObject queryString) => queryString["fields"]?.Select(f => (string)f).ToArray() ?? new string[0];

        [Fact]
        public void InFieldImproved_after_UsingRelevanceImproved_adds_the_field_to_the_main_query()
        {
            var search = For("episerver find")
                .UsingRelevanceImproved(x => x.Title)
                .InFieldImproved(x => x.Title, 2);

            var query = QueryJson(search);

            // The relevance clauses stay: the main query is still the first should clause of the bool query
            Assert.NotNull(query["bool"]);
            var main = QueryStrings(query).First();
            Assert.Equal("episerver find", (string)main["query"]);
            Assert.Contains(Fields(main), f => f.StartsWith("Title") && f.EndsWith("^2"));
            Assert.NotEmpty(query.SelectTokens("$..match_phrase"));
        }

        [Fact]
        public void InFieldImproved_after_FuzzyMatch_keeps_the_fuzzy_clause()
        {
            var search = For("episerver find")
                .FuzzyMatch(x => x.Title)
                .InFieldImproved(x => x.Body);

            var queryStrings = QueryStrings(QueryJson(search));

            Assert.Equal(2, queryStrings.Count);
            Assert.Contains(Fields(queryStrings[0]), f => f.StartsWith("Body"));
            Assert.Contains("~", (string)queryStrings[1]["query"]);
        }

        [Fact]
        public void InFieldImproved_keeps_the_synonym_expansion_and_msm()
        {
            var search = For("episerver find")
                .UsingSynonymsImproved(FindSynonym)
                .MinimumShouldMatch("100%")
                .UsingRelevanceImproved(x => x.Title)
                .InFieldImproved(x => x.Title);

            var main = QueryStrings(QueryJson(search)).First();

            Assert.Equal("episerver (find OR (search AND navigation))", (string)main["query"]);
            Assert.Equal("100%", (string)main["minimum_should_match"]);
            Assert.Contains(Fields(main), f => f.StartsWith("Title"));
        }

        [Fact]
        public void InFieldImproved_writes_the_boost_with_invariant_culture()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");

                var main = QueryStrings(QueryJson(For("episerver").InFieldImproved(x => x.Title, 0.5))).First();

                Assert.Contains(Fields(main), f => f.EndsWith("^0.5"));
                Assert.DoesNotContain(Fields(main), f => f.Contains(","));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Fact]
        public void Same_request_whatever_the_order_of_synonyms_msm_relevance_and_fuzzy()
        {
            var a = For("episerver find").UsingSynonymsImproved(FindSynonym).MinimumShouldMatch("2").UsingRelevanceImproved(x => x.Title).FuzzyMatch(x => x.Title);
            var b = For("episerver find").MinimumShouldMatch("2").UsingSynonymsImproved(FindSynonym).UsingRelevanceImproved(x => x.Title).FuzzyMatch(x => x.Title);

            Assert.Equal(QueryJson(a).ToString(), QueryJson(b).ToString());
        }

        [Fact]
        public void PhraseSlop_option_is_used_for_match_phrase()
        {
            using (FindToolboxOptions.Override(new FindToolboxOptions { PhraseSlop = 5 }))
            {
                var query = QueryJson(For("episerver find").UsingRelevanceImproved(x => x.Title));

                var slops = query.SelectTokens("$..match_phrase.*.slop").Select(t => (int)t).ToList();
                Assert.NotEmpty(slops);
                Assert.All(slops, s => Assert.Equal(5, s));
            }
        }
    }
}
