using System.Collections.Generic;
using System.Linq;
using Optidada.SearchNavigation.Toolbox.Queries;
using Optidada.SearchNavigation.Toolbox.Synonyms;
using Xunit;
using static Optidada.SearchNavigation.Toolbox.UnitTests.TestSearch;

namespace Optidada.SearchNavigation.Toolbox.UnitTests
{
    /// <summary>
    /// Found with the performance benchmark: the Find service rejects (400 "[query] is too long [max:150]") a
    /// query_string query longer than 150 characters when it contains ^ or ~. The whole search then fails.
    /// </summary>
    [Collection("ToolboxOptions")]
    public class QueryLengthTests
    {
        private static Dictionary<string, HashSet<string>> ManySynonyms()
            => FindSynonymProvider.CreateSynonymDictionary(new[] { "initializes", "instance" }
                .SelectMany(w => Enumerable.Range(0, 10).Select(i => TestSynonyms.Create(w, "synonymword" + w.Substring(0, 3) + i, false))));

        [Fact]
        public void Boosted_synonym_expansion_is_trimmed_to_150_characters()
        {
            var result = SynonymExpander.Expand(new[] { "initializes", "instance" }, ManySynonyms());

            Assert.True(result.ExpandedQuery.Length <= 150, result.ExpandedQuery);
            Assert.True(result.SynonymsDropped > 0);
            Assert.StartsWith("(initializes OR ", result.ExpandedQuery);
            Assert.Contains("(instance OR ", result.ExpandedQuery);
            Assert.Equal(2, result.ClauseCount);
        }

        [Fact]
        public void Trimming_drops_synonyms_from_the_largest_group_and_keeps_the_relevance_variations_in_sync()
        {
            var result = SynonymExpander.Expand(new[] { "initializes", "instance" }, ManySynonyms());

            var variations = result.QueriesForMatch.Skip(1).ToList();
            Assert.All(variations, v => Assert.Contains(v.Split(' ').First(w => w.StartsWith("synonymword")), result.ExpandedQuery));
        }

        [Fact]
        public void Unboosted_expansion_is_not_limited()
        {
            // Without ^ the service accepts long queries
            var result = SynonymExpander.Expand(new[] { "initializes", "instance" }, ManySynonyms(), Unboosted);

            Assert.True(result.ExpandedQuery.Length > 150);
            Assert.Equal(0, result.SynonymsDropped);
        }

        [Fact]
        public void Limit_0_turns_trimming_off()
        {
            var result = SynonymExpander.Expand(new[] { "initializes", "instance" }, ManySynonyms(), new ToolboxOptions { MaxBoostedQueryLength = 0 });

            Assert.True(result.ExpandedQuery.Length > 150);
        }

        [Fact]
        public void Fuzzy_query_stays_within_150_characters()
        {
            using (ToolboxOptions.Override(new ToolboxOptions { MaxFuzzyWildcardTerms = 20 }))
            {
                var words = string.Join(" ", Enumerable.Range(0, 20).Select(i => "abcdefghijklm" + (char)('a' + i)));
                var fuzzy = QueryStrings(QueryJson(For(words).FuzzyMatch(x => x.Title))).Single(q => ((string)q["query"]).Contains("~"));

                Assert.True(((string)fuzzy["query"]).Length <= 150);
            }
        }
    }
}
