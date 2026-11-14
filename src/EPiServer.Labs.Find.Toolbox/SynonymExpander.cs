using EPiServer.Find;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EPiServer.Labs.Find.Toolbox
{
    /// <summary>
    /// The result of expanding a user query with synonyms.
    /// </summary>
    public sealed class SynonymExpansion
    {
        internal SynonymExpansion(string[] terms, string expandedQuery, bool hasSynonyms, IReadOnlyList<string> queriesForMatch, int clauseCount)
        {
            ClauseCount = clauseCount;
            Terms = terms;
            ExpandedQuery = expandedQuery;
            HasSynonyms = hasSynonyms;
            QueriesForMatch = queriesForMatch;
        }

        /// <summary>The terms (and quoted phrases) parsed from the user query.</summary>
        public string[] Terms { get; }

        /// <summary>
        /// Query string syntax where every synonym match is replaced by one parenthesized group,
        /// e.g. <c>episerver (find OR (search AND navigation))</c>.
        /// Each group is a single top level clause, which is what makes minimum_should_match
        /// count a term and all of its synonyms as one "term".
        /// </summary>
        public string ExpandedQuery { get; }

        /// <summary>Number of top level clauses in <see cref="ExpandedQuery"/> (a synonym group counts as one).</summary>
        public int ClauseCount { get; }

        /// <summary>True if at least one synonym matched.</summary>
        public bool HasSynonyms { get; }

        /// <summary>
        /// Unescaped plain-text variations of the query (original first, then one variation per synonym)
        /// used by UsingRelevanceImproved() for phrase / prefix boosting.
        /// </summary>
        public IReadOnlyList<string> QueriesForMatch { get; }
    }

    /// <summary>
    /// Pure, side effect free synonym expansion. Kept separate from the Find search pipeline so it can be unit tested.
    /// </summary>
    public static class SynonymExpander
    {
        /// <summary>Longest synonym phrase (in terms) that is looked up.</summary>
        public const int MaxPhraseTerms = 6;

        /// <summary>
        /// Expands <paramref name="terms"/> using <paramref name="synonyms"/>.
        /// Matching is greedy, left to right and longest phrase first, so overlapping phrases are never expanded twice.
        /// Lookups are case insensitive.
        /// </summary>
        public static SynonymExpansion Expand(string[] terms, IDictionary<string, HashSet<string>> synonyms)
        {
            if (terms == null) throw new ArgumentNullException(nameof(terms));

            var queriesForMatch = new List<string> { string.Join(" ", terms) };
            var parts = new List<string>();
            var hasSynonyms = false;

            var i = 0;
            while (i < terms.Length)
            {
                var matched = false;

                if (synonyms != null && synonyms.Count > 0)
                {
                    for (var length = Math.Min(MaxPhraseTerms, terms.Length - i); length >= 1; length--)
                    {
                        var phrase = string.Join(" ", terms, i, length);
                        if (!TryGetSynonyms(synonyms, phrase, out var matchingSynonyms))
                        {
                            continue;
                        }

                        parts.Add(ExpandPhrase(phrase, matchingSynonyms));

                        var before = string.Join(" ", terms.Take(i));
                        var after = string.Join(" ", terms.Skip(i + length));
                        foreach (var synonym in matchingSynonyms)
                        {
                            queriesForMatch.Add(string.Join(" ", new[] { before, synonym, after }.Where(s => s.Length > 0)));
                        }

                        hasSynonyms = true;
                        matched = true;
                        i += length;
                        break;
                    }
                }

                if (!matched)
                {
                    parts.Add(EscapeTerm(terms[i]));
                    i++;
                }
            }

            return new SynonymExpansion(terms, string.Join(" ", parts), hasSynonyms, queriesForMatch.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), parts.Count);
        }

        private static bool TryGetSynonyms(IDictionary<string, HashSet<string>> synonyms, string phrase, out HashSet<string> matchingSynonyms)
        {
            if (synonyms.TryGetValue(phrase, out matchingSynonyms))
            {
                return true;
            }

            return synonyms.TryGetValue(phrase.ToLowerInvariant(), out matchingSynonyms);
        }

        /// <summary>
        /// (phrase OR synonym1 OR synonym2). Multiple term phrases/synonyms that are not quoted
        /// require all of their terms: (search AND navigation).
        /// </summary>
        internal static string ExpandPhrase(string phrase, IEnumerable<string> synonyms)
        {
            var alternatives = new List<string> { ToClause(phrase) };
            alternatives.AddRange(synonyms.Select(ToClause));

            return string.Format("({0})", string.Join(" OR ", alternatives.Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        private static string ToClause(string phrase)
        {
            if (!QueryHelpers.IsStringQuoted(phrase) && ContainsMultipleTerms(phrase))
            {
                // Punctuation only "terms" (e.g. & in "search & navigation") are dropped by the analyzer anyway
                var terms = phrase.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Where(t => t.Any(char.IsLetterOrDigit))
                                  .Select(EscapeTerm)
                                  .ToList();
                return terms.Count == 1 ? terms[0] : string.Format("({0})", string.Join(" AND ", terms));
            }

            return EscapeTerm(phrase);
        }

        /// <summary>Escapes query string syntax in a single term or quoted phrase.</summary>
        internal static string EscapeTerm(string term)
        {
            return term.Quote();
        }

        private static bool ContainsMultipleTerms(string text)
        {
            return text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length > 1;
        }
    }
}
