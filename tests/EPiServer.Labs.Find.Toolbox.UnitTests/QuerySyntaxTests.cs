using System.Linq;
using Xunit;
using static EPiServer.Labs.Find.Toolbox.UnitTests.TestSearch;

namespace EPiServer.Labs.Find.Toolbox.UnitTests
{
    /// <summary>
    /// Query syntax. Find's For() escapes nearly everything (-, +, :, *, ~, parentheses, !), so only quoted phrases and
    /// upper case AND / OR / NOT are real syntax. These tests pin both Find's behaviour and the toolbox's.
    /// </summary>
    [Collection("FindToolboxOptions")]
    public class QuerySyntaxTests
    {
        private static readonly UsingSynonymService Synonyms_ = Synonyms(("cats", "felines", false), ("e-mail", "email", false), ("c++", "cpp", false));

        [Theory]
        [InlineData("cats -dogs", @"cats \-dogs")]
        [InlineData("+cats dogs", @"\+cats dogs")]
        [InlineData("title:x", @"title\:x")]
        [InlineData("foo*", @"foo\*")]
        [InlineData("episervr~", @"episervr\~")]
        [InlineData("e-mail", @"e\-mail")]
        [InlineData("c++", @"c\+\+")]
        [InlineData("(a b)", @"\(a b\)")]
        [InlineData("\"new york\" pizza", "\"new york\" pizza")]
        [InlineData("cats OR dogs", "cats OR dogs")]
        [InlineData("cats NOT dogs", "cats NOT dogs")]
        public void Find_For_escapes_everything_but_phrases_and_boolean_operators(string input, string sent)
        {
            Assert.Equal(sent, (string)QueryStrings(QueryJson(For(input))).Single()["query"]);
        }

        [Theory]
        [InlineData("cats OR dogs", true)]
        [InlineData("cats AND dogs", true)]
        [InlineData("NOT dogs", true)]
        [InlineData("cats NOT", true)]
        [InlineData("cats or dogs", false)]        // lower case is not an operator in query_string
        [InlineData("ORegon NOTebook", false)]
        [InlineData("\"cats OR dogs\"", false)]   // inside a phrase
        [InlineData("e-mail wi-fi t-shirt", false)]
        [InlineData("cats -dogs +mice", false)]    // escaped by For(): literals
        [InlineData("c++ 2+2 covid-19", false)]
        [InlineData("", false)]
        public void Boolean_operators_are_detected(string query, bool expected)
        {
            Assert.Equal(expected, QueryHelpers.HasBooleanOperators(query));
        }

        [Theory]
        [InlineData("e-mail marketing", "(e-mail OR email)")]
        [InlineData("c++ jobs", "(c++ OR cpp)")]
        public void Hyphen_and_plus_words_still_get_synonyms(string query, string expectedGroupUnescaped)
        {
            var json = QueryJson(For(query).MinimumShouldMatch("100%").UsingSynonymsImproved(Synonyms_));
            var sent = ((string)QueryStrings(json).First()["query"]).Replace("\\", string.Empty);

            Assert.Contains(expectedGroupUnescaped, sent);
        }

        [Theory]
        [InlineData("cats NOT dogs")]
        [InlineData("cats OR dogs")]
        [InlineData("cats AND dogs")]
        public void Queries_with_operators_are_left_exactly_as_Find_sends_them(string query)
        {
            var plain = QueryJson(For(query).MinimumShouldMatch("100%")).ToString();
            var withToolbox = QueryJson(For(query).MinimumShouldMatch("100%")
                .UsingSynonymsImproved(Synonyms_)
                .UsingRelevanceImproved(x => x.Title)
                .FuzzyMatch(x => x.Title)
                .WildcardMatch(x => x.Title)).ToString();

            // no synonyms, no phrase/prefix/fuzzy/wildcard clauses that could match excluded documents
            Assert.Equal(plain, withToolbox);
        }

        [Fact]
        public void Quoted_phrase_with_operator_words_is_not_treated_as_operators()
        {
            var json = QueryJson(For("\"cats OR dogs\" cats").MinimumShouldMatch("100%").UsingSynonymsImproved(Synonyms_));

            Assert.Contains("felines", (string)QueryStrings(json).First()["query"]);
        }
    }
}
