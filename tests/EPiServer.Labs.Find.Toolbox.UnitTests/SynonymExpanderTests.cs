using EPiServer.Find;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace EPiServer.Labs.Find.Toolbox.UnitTests
{
    public class SynonymExpanderTests
    {
        private static Dictionary<string, HashSet<string>> Dictionary(params (string phrase, string synonym, bool bidirectional)[] synonyms)
            => SynonymLoader.CreateSynonymDictionary(synonyms.Select(s => TestSynonyms.Create(s.phrase, s.synonym, s.bidirectional)));

        private static SynonymExpansion Expand(string query, params (string, string, bool)[] synonyms)
            => SynonymExpander.Expand(QueryHelpers.GetQueryPhrases(query), Dictionary(synonyms));

        [Fact]
        public void No_synonyms_returns_the_terms()
        {
            var result = Expand("episerver find");

            Assert.False(result.HasSynonyms);
            Assert.Equal("episerver find", result.ExpandedQuery);
        }

        [Fact]
        public void Single_term_synonym_becomes_one_parenthesized_clause()
        {
            var result = Expand("episerver find", ("find", "search & navigation", false));

            Assert.True(result.HasSynonyms);
            Assert.Equal("episerver (find OR (search AND navigation))", result.ExpandedQuery);
        }

        [Fact]
        public void Multi_term_phrase_requires_all_its_terms()
        {
            var result = Expand("i love new york", ("new york", "nyc", false));

            Assert.Equal("i love ((new AND york) OR nyc)", result.ExpandedQuery);
        }

        [Fact]
        public void Longest_phrase_wins()
        {
            var result = Expand("new york city",
                ("new york", "nyc", false),
                ("york city", "yc", false),
                ("new york city", "big apple", false));

            Assert.Equal("((new AND york AND city) OR (big AND apple))", result.ExpandedQuery);
        }

        [Fact]
        public void Overlapping_phrases_are_not_expanded_twice()
        {
            var result = Expand("new york city",
                ("new york", "nyc", false),
                ("york city", "yc", false));

            // "york" must only appear once: it belongs to the first (left most, longest) match
            Assert.Equal("((new AND york) OR nyc) city", result.ExpandedQuery);
        }

        [Theory]
        [InlineData("FIND")]
        [InlineData("Find")]
        [InlineData("find")]
        public void Lookup_is_case_insensitive(string query)
        {
            var result = Expand(query, ("Find", "Search", false));

            Assert.True(result.HasSynonyms);
            Assert.Contains("OR search)", result.ExpandedQuery);
        }

        [Fact]
        public void Bidirectional_synonym_expands_both_ways()
        {
            Assert.True(Expand("automobile", ("car", "automobile", true)).HasSynonyms);
            Assert.True(Expand("car", ("car", "automobile", true)).HasSynonyms);
        }

        [Fact]
        public void One_way_synonym_only_expands_from_the_phrase()
        {
            Assert.True(Expand("car", ("car", "automobile", false)).HasSynonyms);
            Assert.False(Expand("automobile", ("car", "automobile", false)).HasSynonyms);
        }

        [Fact]
        public void Comma_separated_phrases_each_get_the_synonym()
        {
            var dictionary = Dictionary(("find, s&n", "search", false));

            Assert.Contains("find", dictionary.Keys);
            Assert.Contains("s&n", dictionary.Keys);
        }

        [Fact]
        public void Repeated_terms_are_kept()
        {
            var result = Expand("find find", ("find", "search", false));

            Assert.Equal("(find OR search) (find OR search)", result.ExpandedQuery);
        }

        [Fact]
        public void Queries_for_match_contain_original_and_each_synonym_variation()
        {
            var result = Expand("episerver find", ("find", "search", false), ("find", "navigation", false));

            Assert.Equal("episerver find", result.QueriesForMatch[0]);
            Assert.Contains("episerver search", result.QueriesForMatch);
            Assert.Contains("episerver navigation", result.QueriesForMatch);
        }

        [Fact]
        public void Query_string_syntax_in_terms_is_escaped()
        {
            var result = Expand("c# find", ("find", "search", false));

            Assert.StartsWith("c#".Quote() + " ", result.ExpandedQuery);
        }

        [Fact]
        public void Synonym_to_itself_is_ignored()
        {
            Assert.False(Expand("find", ("find", "FIND", false)).HasSynonyms);
        }

        [Theory]
        [InlineData("episerver   find", new[] { "episerver", "find" })]
        [InlineData("  episerver\tfind ", new[] { "episerver", "find" })]
        [InlineData("cats AND dogs", new[] { "cats", "dogs" })]
        [InlineData("\"new york\" pizza", new[] { "\"new york\"", "pizza" })]
        [InlineData("c# c++ .net", new[] { "c#", "c++", ".net" })]
        [InlineData("s&n e-mail asp.net", new[] { "s&n", "e-mail", "asp.net" })]
        [InlineData("version 2.0.1 costs 1,5", new[] { "version", "2.0.1", "costs", "1,5" })]
        [InlineData("find. search,", new[] { "find", "search" })]
        [InlineData("", new string[0])]
        public void Query_phrases_are_parsed(string query, string[] expected)
        {
            Assert.Equal(expected, QueryHelpers.GetQueryPhrases(query));
        }
    }
}
