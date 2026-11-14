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
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IDictionary<string, HashSet<string>>, object> LongestPhraseCache
            = new System.Runtime.CompilerServices.ConditionalWeakTable<IDictionary<string, HashSet<string>>, object>();

        /// <summary>
        /// Expands <paramref name="terms"/> using <paramref name="synonyms"/>.
        /// Matching is greedy, left to right and longest phrase first, so overlapping phrases are never expanded twice.
        /// Lookups are case insensitive. Expansion is bounded by <paramref name="options"/> (defaults: <see cref="FindToolboxOptions"/>).
        /// </summary>
        public static SynonymExpansion Expand(string[] terms, IDictionary<string, HashSet<string>> synonyms, FindToolboxOptions options = null)
        {
            if (terms == null) throw new ArgumentNullException(nameof(terms));
            options = options ?? FindToolboxOptions.Defaults;

            var queriesForMatch = new List<string> { string.Join(" ", terms) };
            var parts = new List<string>();
            var expandedPhrases = 0;
            var maxPhraseTerms = synonyms != null && synonyms.Count > 0
                ? Math.Min(Math.Max(1, options.MaxSynonymPhraseTerms), GetLongestPhrase(synonyms))
                : 0;

            var i = 0;
            while (i < terms.Length)
            {
                var matched = false;

                if (maxPhraseTerms > 0 && expandedPhrases < options.MaxExpandedPhrases)
                {
                    for (var length = Math.Min(maxPhraseTerms, terms.Length - i); length >= 1; length--)
                    {
                        var phrase = string.Join(" ", terms, i, length);
                        if (!TryGetSynonyms(synonyms, phrase, out var allSynonyms))
                        {
                            continue;
                        }

                        var matchingSynonyms = allSynonyms.Take(Math.Max(1, options.MaxSynonymsPerPhrase)).ToList();
                        parts.Add(ExpandPhrase(phrase, matchingSynonyms, options.SynonymBoost));

                        var before = string.Join(" ", terms.Take(i));
                        var after = string.Join(" ", terms.Skip(i + length));
                        foreach (var synonym in matchingSynonyms)
                        {
                            queriesForMatch.Add(string.Join(" ", new[] { before, synonym, after }.Where(s => s.Length > 0)));
                        }

                        expandedPhrases++;
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

            return new SynonymExpansion(terms, string.Join(" ", parts), expandedPhrases > 0, queriesForMatch.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), parts.Count);
        }

        /// <summary>Longest phrase (in terms) in the synonym list; computed once per dictionary instance.</summary>
        internal static int GetLongestPhrase(IDictionary<string, HashSet<string>> synonyms)
        {
            return (int)LongestPhraseCache.GetValue(synonyms, d => d.Keys.Count == 0
                ? 0
                : d.Keys.Max(k => k.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length));
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
        internal static string ExpandPhrase(string phrase, IEnumerable<string> synonyms, double synonymBoost = 1)
        {
            var phraseTerms = CountTerms(phrase);

            var alternatives = new List<string> { ToClause(phrase) };
            alternatives.AddRange(synonyms.Select(s => ToClause(s) + BoostSuffix(synonymBoost, phraseTerms, CountTerms(s))));

            return string.Format("({0})", string.Join(" OR ", alternatives.Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Synonym boost, normalized by length: a synonym with more terms than the phrase it replaces would otherwise
        /// score higher just because more terms match (e.g. find => search &amp; navigation).
        /// </summary>
        private static string BoostSuffix(double synonymBoost, int phraseTerms, int synonymTerms)
        {
            // 1 = synonyms weigh the same as the original (upstream behaviour): no boost, no normalization
            if (Math.Abs(synonymBoost - 1) < 0.0001)
            {
                return string.Empty;
            }

            var boost = synonymBoost * Math.Min(1.0, (double)Math.Max(1, phraseTerms) / Math.Max(1, synonymTerms));
            return Math.Abs(boost - 1) < 0.0001
                ? string.Empty
                : "^" + boost.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int CountTerms(string phrase)
        {
            if (QueryHelpers.IsStringQuoted(phrase))
            {
                phrase = phrase.Trim('"');
            }

            return Math.Max(1, phrase.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Count(t => t.Any(char.IsLetterOrDigit)));
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
