using System.Collections.Generic;
using System.Linq;
using EPiServer.Find;
using Optidada.SearchNavigation.Toolbox.Queries;
using Optidada.SearchNavigation.Toolbox.Synonyms;
using Xunit;
using static Optidada.SearchNavigation.Toolbox.UnitTests.TestSearch;

namespace Optidada.SearchNavigation.Toolbox.UnitTests
{
    /// <summary>Every expansion limit in <see cref="ToolboxOptions"/> is enforced.</summary>
    [Collection("ToolboxOptions")] // some tests override the global options
    public class LimitTests
    {
        private static Dictionary<string, HashSet<string>> Dictionary(params (string phrase, string synonym, bool bidirectional)[] synonyms)
            => FindSynonymProvider.CreateSynonymDictionary(synonyms.Select(s => TestSynonyms.Create(s.phrase, s.synonym, s.bidirectional)));

        private static int Count(string text, string value) => (text.Length - text.Replace(value, "").Length) / value.Length;

        [Fact]
        public void Defaults_are_the_documented_values()
        {
            var o = new ToolboxOptions();
            Assert.Equal(50, o.MaxQueryTerms);
            Assert.Equal(10, o.MaxSynonymPhraseTerms);
            Assert.Equal(10, o.MaxSynonymsPerPhrase);
            Assert.Equal(5, o.MaxExpandedPhrases);
            Assert.Equal(3, o.MaxQueriesForMatch);
            Assert.Equal(10, o.MaxRelevanceTerms);
            Assert.Equal(3, o.MinPrefixLength);
            Assert.Equal(3, o.MinFuzzyWildcardTermLength);
            Assert.Equal(16, o.MaxFuzzyWildcardTermLength);
            Assert.Equal(3, o.MaxFuzzyWildcardTerms);
            Assert.Equal(1, o.FuzzyPrefixLength);
        }

        [Fact]
        public void Query_terms_are_capped()
        {
            var query = string.Join(" ", Enumerable.Range(1, 80).Select(i => "t" + i));
            Assert.Equal(50, QueryText.GetQueryPhrases(query, 50).Length);
            Assert.Equal(5, QueryText.GetQueryPhrases(query, 5).Length);
        }

        [Fact]
        public void Synonyms_per_phrase_are_capped()
        {
            var synonyms = Enumerable.Range(1, 30).Select(i => ("find", "syn" + i, false)).ToArray();

            var result = SynonymExpander.Expand(new[] { "find" }, Dictionary(synonyms), new ToolboxOptions { MaxSynonymsPerPhrase = 10, SynonymBoost = 1 });

            Assert.Equal(10, Count(result.ExpandedQuery, "syn"));
            Assert.Equal("(find OR syn1 OR syn2 OR syn3 OR syn4 OR syn5 OR syn6 OR syn7 OR syn8 OR syn9 OR syn10)", result.ExpandedQuery);
        }

        [Fact]
        public void Expanded_phrases_per_query_are_capped_and_later_matches_stay_plain()
        {
            var dictionary = Dictionary(("a1", "x", false), ("a2", "x", false), ("a3", "x", false));

            var result = SynonymExpander.Expand(new[] { "a1", "a2", "a3" }, dictionary, new ToolboxOptions { MaxExpandedPhrases = 2, SynonymBoost = 1 });

            Assert.Equal("(a1 OR x) (a2 OR x) a3", result.ExpandedQuery);
            Assert.Equal(3, result.ClauseCount);
        }

        [Fact]
        public void Phrase_length_follows_the_longest_synonym_phrase()
        {
            var seven = "one two three four five six seven";
            var dictionary = Dictionary((seven, "long", false));

            Assert.True(SynonymExpander.Expand(seven.Split(' '), dictionary).HasSynonyms);
            Assert.False(SynonymExpander.Expand(seven.Split(' '), dictionary, new ToolboxOptions { MaxSynonymPhraseTerms = 6 }).HasSynonyms);
            Assert.Equal(7, SynonymExpander.GetLongestPhrase(dictionary));
        }

        [Fact]
        public void Queries_for_match_are_capped()
        {
            var service = new SynonymService(
                new FakeSynonymProvider(("find", "s1", false), ("find", "s2", false), ("find", "s3", false), ("find", "s4", false)),
                new ToolboxOptions { MaxQueriesForMatch = 2 });

            var phrases = QueryJson(For("episerver find").UsingSynonymsImproved(service).UsingRelevanceImproved(x => x.Title))
                .SelectTokens("$..match_phrase.*.query").Select(t => (string)t).ToList();

            Assert.Equal(new[] { "episerver find", "episerver s1" }, phrases);
        }

        [Fact]
        public void Relevance_phrase_terms_are_capped()
        {
            using (ToolboxOptions.Override(new ToolboxOptions { MaxRelevanceTerms = 3 }))
            {
                var phrase = QueryJson(For("a1 a2 a3 a4 a5").UsingRelevanceImproved(x => x.Title))
                    .SelectTokens("$..match_phrase.*.query").Select(t => (string)t).Single();

                Assert.Equal("a1 a2 a3", phrase);
            }
        }

        [Theory]
        [InlineData("ab", false)]
        [InlineData("abc", true)]
        public void Prefix_query_needs_MinPrefixLength_characters(string query, bool expectPrefix)
        {
            var json = QueryJson(For(query).UsingRelevanceImproved(x => x.Title));

            Assert.Equal(expectPrefix, json.SelectTokens("$..prefix").Any());
        }

        [Fact]
        public void Fuzzy_terms_respect_length_and_count()
        {
            var json = QueryJson(For("ab abc abcdefghijklmnopq t1x t2x t3x t4x").FuzzyMatch(x => x.Title));
            var fuzzy = (string)QueryStrings(json).Single(q => ((string)q["query"]).Contains("~"))["query"];

            // "ab" too short, the 17 char term too long, max 3 terms
            Assert.Equal("abc~ t1x~ t2x~", fuzzy);
        }

        [Fact]
        public void Fuzzy_prefix_length_comes_from_options()
        {
            using (ToolboxOptions.Override(new ToolboxOptions { FuzzyPrefixLength = 3 }))
            {
                var json = QueryJson(For("episerver").FuzzyMatch(x => x.Title));
                var fuzzy = QueryStrings(json).Single(q => ((string)q["query"]).Contains("~"));

                Assert.Equal(3, (int)fuzzy["fuzzy_prefix_length"]);
            }
        }

        [Fact]
        public void Fuzzy_and_wildcard_use_the_language_field_when_the_search_has_a_language()
        {
            var search = TestSearch.Client.Search<Doc>(EPiServer.Find.Language.Swedish).For("kaffekopp").FuzzyMatch(x => x.Title).WildcardMatch(x => x.Title);
            var json = QueryJson(search);

            foreach (var q in QueryStrings(json).Where(q => ((string)q["query"]).Contains("~") || ((string)q["query"]).Contains("*")))
            {
                var fields = q["fields"].Select(f => (string)f).ToList();
                Assert.Contains(fields, f => f.EndsWith(".sv"));        // stemming / decompounding
                Assert.Contains(fields, f => f.EndsWith(".standard"));  // content in other languages
            }
        }

        [Fact]
        public void Fuzzy_and_wildcard_without_language_use_the_language_neutral_field()
        {
            var json = QueryJson(For("episerver").FuzzyMatch(x => x.Title));
            var fuzzy = QueryStrings(json).Single(q => ((string)q["query"]).Contains("~"));

            Assert.All(fuzzy["fields"].Select(f => (string)f), f => Assert.DoesNotContain(".sv", f));
        }

        [Fact]
        public void Wildcard_terms_respect_length_and_count()
        {
            var json = QueryJson(For("ab abc t1x t2x t3x").WildcardMatch(x => x.Title));
            var wildcard = (string)QueryStrings(json).Single(q => ((string)q["query"]).Contains("*"))["query"];

            Assert.Equal("abc* t1x* t2x*", wildcard);
        }
    }

    [CollectionDefinition("ToolboxOptions", DisableParallelization = true)]
    public class FindToolboxOptionsCollection { }
}
