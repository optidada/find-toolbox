using System;
using System.Linq;
using System.Text.RegularExpressions;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;

namespace Optidada.SearchNavigation.Toolbox.Queries
{
    /// <summary>Parsing of the user's query text: terms, quoted phrases and AND / OR / NOT.</summary>
    internal static class QueryText
    {
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        // Quoted phrases | numbers with separators (1,5 / 2.0.1) | terms. Terms may start with a dot (.net)
        // and contain - & ´ ' ` . / : # + (c#, c++, s&n, e-mail, asp.net).
        private static readonly Regex TermsAndQuotedPhrases = new Regex(@"(""[^""]+"")|(\d[\d\.\,]*\d)|(\.?\w[\w\-&´'`\.\/:#\+]*)", RegexOptions.Compiled);

        private static readonly Regex QuotedPhrase = new Regex(@"""[^""]*""", RegexOptions.Compiled);
        private static readonly Regex BooleanOperator = new Regex(@"(^|\s)(AND|OR|NOT)(\s|$)", RegexOptions.Compiled);
        private static readonly string[] BooleanOperators = { "AND", "OR", "NOT" };
        private static readonly char[] TrailingPunctuation = { '.', ',', ':', '/', '-' };

        /// <summary>What the user typed, as recorded by search statistics; never changed by the toolbox.</summary>
        public static string RawQuery(IQuery query) => (query as QueryStringQuery)?.RawQuery?.ToString() ?? string.Empty;

        /// <summary>The terms and quoted phrases of <paramref name="query"/> (at most <see cref="ToolboxOptions.MaxQueryTerms"/>).</summary>
        public static string[] GetQueryPhrases(string query) => GetQueryPhrases(query, ToolboxOptions.Current.MaxQueryTerms);

        /// <summary>The terms and quoted phrases of <paramref name="query"/>, without AND / OR / NOT and trailing punctuation.</summary>
        public static string[] GetQueryPhrases(string query, int maxTerms)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new string[0];
            }

            return TermsAndQuotedPhrases.Matches(Whitespace.Replace(query, " ").Trim()).Cast<Match>()
                .Select(m => m.Value.Trim())
                .Select(t => t.StartsWith("\"") ? Whitespace.Replace(t, " ") : t.TrimEnd(TrailingPunctuation))
                .Where(t => t.Length > 0 && !BooleanOperators.Contains(t))
                .Take(Math.Max(1, maxTerms))
                .ToArray();
        }

        /// <summary>
        /// True if the query uses boolean operators: a standalone, upper case AND, OR or NOT outside quotes.
        /// Find's For() escapes all other query syntax (-, +, *, ~, :, parentheses...), so hyphens and pluses
        /// inside or in front of words (e-mail, c++, -dogs) are never operators.
        /// </summary>
        public static bool HasBooleanOperators(string query)
            => !string.IsNullOrEmpty(query) && BooleanOperator.IsMatch(QuotedPhrase.Replace(query, " "));

        /// <summary>True when the user's query uses AND / OR / NOT and the options say to respect them.</summary>
        public static bool RespectsBooleanOperators(string rawQuery, ToolboxOptions options)
            => options.BooleanOperators == BooleanOperatorHandling.Respect && HasBooleanOperators(rawQuery);

        public static bool IsQuoted(string text) => text != null && text.Length >= 2 && text.StartsWith("\"") && text.EndsWith("\"");

        public static bool IsParenthesized(string text) => text != null && text.Length >= 2 && text.StartsWith("(") && text.EndsWith(")");
    }
}
