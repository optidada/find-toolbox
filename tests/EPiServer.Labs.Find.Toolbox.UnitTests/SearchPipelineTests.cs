using EPiServer.Find;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using Xunit;
using static EPiServer.Labs.Find.Toolbox.UnitTests.TestSearch;

namespace EPiServer.Labs.Find.Toolbox.UnitTests
{
    /// <summary>
    /// Asserts the Elasticsearch request JSON that the toolbox generates. No Find index involved.
    /// </summary>
    public class SearchPipelineTests
    {
        private static readonly UsingSynonymService FindSynonym = Synonyms(("find", "search & navigation", false));

        private static string Operator(JObject queryString) => ((string)queryString["default_operator"] ?? "OR").ToUpperInvariant();

        [Fact]
        public void MinimumShouldMatch_with_synonyms_is_ONE_query_string_carrying_the_msm()
        {
            var search = For("episerver find").MinimumShouldMatch("2<60%").UsingSynonymsImproved(FindSynonym);

            var queryStrings = QueryStrings(QueryJson(search));

            // Upstream 2.x emitted two OR'ed clauses (terms w/ msm + synonyms w/ msm=1) which let
            // documents through that only matched the synonym group.
            var single = Assert.Single(queryStrings);
            Assert.Equal("episerver (find OR (search AND navigation))", (string)single["query"]);
            Assert.Equal("2<60%", (string)single["minimum_should_match"]);
            Assert.Equal("OR", Operator(single));
        }

        [Fact]
        public void MinimumShouldMatch_can_be_called_after_UsingSynonymsImproved()
        {
            var search = For("episerver find").UsingSynonymsImproved(FindSynonym).MinimumShouldMatch("100%");

            var single = Assert.Single(QueryStrings(QueryJson(search)));
            Assert.Equal("episerver (find OR (search AND navigation))", (string)single["query"]);
            Assert.Equal("100%", (string)single["minimum_should_match"]);
        }

        [Fact]
        public void And_operator_with_synonyms_stays_AND()
        {
            // Upstream 1.x silently turned this into OR when MinimumShouldMatch() was not used
            var search = For("episerver find").WithAndAsDefaultOperator().UsingSynonymsImproved(FindSynonym);

            var single = Assert.Single(QueryStrings(QueryJson(search)));
            Assert.Equal("episerver (find OR (search AND navigation))", (string)single["query"]);
            Assert.Equal("AND", Operator(single));
            Assert.Null(single["minimum_should_match"]);
        }

        [Fact]
        public void Default_or_with_synonyms_stays_OR_without_msm()
        {
            var search = For("episerver find").UsingSynonymsImproved(FindSynonym);

            var single = Assert.Single(QueryStrings(QueryJson(search)));
            Assert.Equal("OR", Operator(single));
            Assert.Null(single["minimum_should_match"]);
        }

        [Fact]
        public void MinimumShouldMatch_overrides_and_operator()
        {
            var search = For("episerver find").WithAndAsDefaultOperator().MinimumShouldMatch("1").UsingSynonymsImproved(FindSynonym);

            var single = Assert.Single(QueryStrings(QueryJson(search)));
            Assert.Equal("OR", Operator(single));
            Assert.Equal("1", (string)single["minimum_should_match"]);
        }

        [Fact]
        public void Without_synonym_match_the_users_query_is_left_untouched()
        {
            var search = For("cats NOT dogs").MinimumShouldMatch("2").UsingSynonymsImproved(FindSynonym);

            var single = Assert.Single(QueryStrings(QueryJson(search)));
            Assert.Equal("cats NOT dogs", (string)single["query"]);
            Assert.Equal("2", (string)single["minimum_should_match"]);
        }

        [Fact]
        public void Fields_from_InField_are_kept()
        {
            var search = For("episerver find").InField(x => x.Title).MinimumShouldMatch("2").UsingSynonymsImproved(FindSynonym);

            var single = Assert.Single(QueryStrings(QueryJson(search)));
            var fields = single["fields"].Select(f => (string)f).ToList();
            Assert.Contains(fields, f => f.StartsWith("Title"));
        }

        [Fact]
        public void UsingRelevanceImproved_without_synonyms_does_not_throw()
        {
            // Upstream threw NullReferenceException unless UsingSynonymsImproved() had been called first
            var search = For("episerver find").UsingRelevanceImproved(x => x.Title);

            var query = QueryJson(search);
            Assert.NotEmpty(query.SelectTokens("$..match_phrase"));
        }

        [Fact]
        public void UsingRelevanceImproved_adds_phrase_boosts_for_each_synonym_variation()
        {
            var search = For("episerver find").MinimumShouldMatch("2").UsingSynonymsImproved(FindSynonym).UsingRelevanceImproved(x => x.Title);

            var phrases = QueryJson(search).SelectTokens("$..match_phrase.*.query").Select(t => (string)t).ToList();
            Assert.Contains("episerver find", phrases);
            Assert.Contains("episerver search & navigation", phrases);
        }

        [Fact]
        public void UsingRelevanceImproved_keeps_main_query_first_with_its_msm()
        {
            var search = For("episerver find").MinimumShouldMatch("2").UsingSynonymsImproved(FindSynonym).UsingRelevanceImproved(x => x.Title);

            var first = QueryStrings(QueryJson(search)).First();
            Assert.Equal("2", (string)first["minimum_should_match"]);
            Assert.Equal("episerver (find OR (search AND navigation))", (string)first["query"]);
        }

        [Fact]
        public void Prefix_query_values_are_not_query_string_escaped()
        {
            var search = For("c#").UsingRelevanceImproved(x => x.Title);

            var prefixValues = QueryJson(search).SelectTokens("$..prefix.*").Select(t => t.Type == JTokenType.Object ? (string)t["value"] ?? (string)t["prefix"] : (string)t).ToList();
            Assert.Contains("c#", prefixValues);
        }

        [Fact]
        public void FuzzyMatch_with_default_or_does_not_force_100_percent()
        {
            var search = For("episerver find").FuzzyMatch(x => x.Title);

            var fuzzy = QueryStrings(QueryJson(search)).Single(q => ((string)q["query"]).Contains("~"));
            Assert.Null(fuzzy["minimum_should_match"]);
        }

        [Fact]
        public void FuzzyMatch_inherits_msm()
        {
            var search = For("episerver find").MinimumShouldMatch("2<60%").FuzzyMatch(x => x.Title);

            var fuzzy = QueryStrings(QueryJson(search)).Single(q => ((string)q["query"]).Contains("~"));
            Assert.Equal("2<60%", (string)fuzzy["minimum_should_match"]);
        }

        [Fact]
        public void Full_chain_from_readme_builds()
        {
            var search = For("episerver find")
                .MinimumShouldMatch("2<60%")
                .UsingSynonymsImproved(FindSynonym)
                .UsingRelevanceImproved(x => x.Title)
                .FuzzyMatch(x => x.Title)
                .WildcardMatch(x => x.Title);

            var query = QueryJson(search);
            Assert.Equal("episerver (find OR (search AND navigation))", (string)QueryStrings(query).First()["query"]);
        }
    }
}
