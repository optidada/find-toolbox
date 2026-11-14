using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static EPiServer.Labs.Find.Toolbox.UnitTests.TestSearch;

namespace EPiServer.Labs.Find.Toolbox.UnitTests
{
    /// <summary>
    /// The toolbox only rewrites Query; RawQuery (what the user typed) is what Find search statistics record,
    /// so it must reach the request unchanged whatever extensions are used.
    /// </summary>
    [Collection("FindToolboxOptions")]
    public class RawQueryTests
    {
        private static readonly UsingSynonymService FindSynonym = Synonyms(("find", "search & navigation", false));

        public static IEnumerable<object[]> Chains()
        {
            var chains = new Dictionary<string, Func<IQueriedSearch<Doc, QueryStringQuery>, IQueriedSearch<Doc, QueryStringQuery>>>
            {
                ["MinimumShouldMatch"] = s => s.MinimumShouldMatch("2"),
                ["UsingSynonymsImproved"] = s => s.UsingSynonymsImproved(FindSynonym),
                ["MinimumShouldMatch + synonyms"] = s => s.MinimumShouldMatch("100%").UsingSynonymsImproved(FindSynonym),
                ["synonyms + MinimumShouldMatch"] = s => s.UsingSynonymsImproved(FindSynonym).MinimumShouldMatch("100%"),
                ["AND + synonyms"] = s => s.WithAndAsDefaultOperator().UsingSynonymsImproved(FindSynonym),
                ["InField + synonyms"] = s => s.InField(x => x.Title).MinimumShouldMatch("2").UsingSynonymsImproved(FindSynonym),
                ["UsingRelevanceImproved"] = s => s.UsingRelevanceImproved(x => x.Title),
                ["FuzzyMatch"] = s => s.FuzzyMatch(x => x.Title),
                ["WildcardMatch"] = s => s.WildcardMatch(x => x.Title),
                ["full README chain"] = s => s.MinimumShouldMatch("2<-25%").UsingSynonymsImproved(FindSynonym)
                                              .UsingRelevanceImproved(x => x.Title).FuzzyMatch(x => x.Title).WildcardMatch(x => x.Title),
            };

            return chains.Select(c => new object[] { c.Key, c.Value });
        }

        [Theory]
        [MemberData(nameof(Chains))]
        public void RawQuery_is_the_users_text(string chain, Func<IQueriedSearch<Doc, QueryStringQuery>, IQueriedSearch<Doc, QueryStringQuery>> apply)
        {
            const string typed = "Episerver  find";

            var context = new SearchContext();
            apply(For(typed)).ApplyActions(context);

            // The main query: the request query itself, or the first should clause when the toolbox wrapped it in a bool query
            var main = context.RequestBody.Query is BoolQuery boolQuery ? boolQuery.Should.First() : context.RequestBody.Query;
            var queryString = Assert.IsAssignableFrom<QueryStringQuery>(main);

            Assert.Equal(typed, queryString.RawQuery?.ToString());
            Assert.NotNull(chain);
        }

        [Fact]
        public void Query_is_rewritten_but_RawQuery_is_not()
        {
            var context = new SearchContext();
            For("episerver find").MinimumShouldMatch("100%").UsingSynonymsImproved(FindSynonym).ApplyActions(context);

            var queryString = (QueryStringQuery)context.RequestBody.Query;
            Assert.Contains("search AND navigation", queryString.Query.ToString());
            Assert.Equal("episerver find", queryString.RawQuery.ToString());
        }
    }
}
