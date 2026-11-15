using System;
using System.Collections.Generic;
using System.Linq;
using EPiServer.Find;
using Optidada.SearchNavigation.Toolbox.Queries;

namespace Optidada.SearchNavigation.Toolbox.Synonyms
{
    /// <summary>
    /// The result of expanding a user query with synonyms.
    /// </summary>
    internal sealed class SynonymExpansion
    {
        internal SynonymExpansion(string[] terms, string expandedQuery, bool hasSynonyms, IReadOnlyList<string> queriesForMatch, int clauseCount, int synonymsDropped = 0)
        {
            SynonymsDropped = synonymsDropped;
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

        /// <summary>Synonyms left out to keep the query within <see cref="ToolboxOptions.MaxBoostedQueryLength"/>.</summary>
        public int SynonymsDropped { get; }
    }

    /// <summary>
    /// Pure, side effect free synonym expansion. Kept separate from the Find search pipeline so it can be unit tested.
    /// </summary>
    internal static class SynonymExpander
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IDictionary<string, HashSet<string>>, object> LongestPhraseCache
            = new System.Runtime.CompilerServices.ConditionalWeakTable<IDictionary<string, HashSet<string>>, object>();

        /// <summary>
        /// Expands <paramref name="terms"/> using <paramref name="synonyms"/>.
        /// Matching is greedy, left to right and longest phrase first, so overlapping phrases are never expanded twice.
        /// Lookups are case insensitive. Expansion is bounded by <paramref name="options"/> (defaults: <see cref="ToolboxOptions"/>).
        /// </summary>
        public static SynonymExpansion Expand(string[] terms, IDictionary<string, HashSet<string>>? synonyms, ToolboxOptions? options = null)
            => Expand(terms, synonyms, options, stem: null, languageKey: null);

        /// <summary>
        /// As <see cref="Expand(string[], IDictionary{string, HashSet{string}}, ToolboxOptions)"/>, also matching synonyms
        /// by word stem when <paramref name="stem"/> is given (see <see cref="SynonymStemming"/>).
        /// </summary>
        public static SynonymExpansion Expand(string[] terms, IDictionary<string, HashSet<string>>? synonyms, ToolboxOptions? options, Func<string, string>? stem, string? languageKey)
        {
            if (terms == null) throw new ArgumentNullException(nameof(terms));
            options = options ?? ToolboxOptions.Defaults;

            var segments = new List<Segment>();
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
                        if (!TryGetSynonyms(synonyms!, phrase, stem, languageKey, out var allSynonyms))
                        {
                            continue;
                        }

                        segments.Add(new Segment(i, length, phrase, allSynonyms.Take(Math.Max(1, options.MaxSynonymsPerPhrase)).ToList()));
                        expandedPhrases++;
                        matched = true;
                        i += length;
                        break;
                    }
                }

                if (!matched)
                {
                    segments.Add(new Segment(i, 1, terms[i], new List<string>()));
                    i++;
                }
            }

            // The Find service rejects a query_string query with boosts (^) or fuzzy (~) longer than 150 characters (400,
            // the search fails): drop synonyms - from the group with the most synonyms, later groups first - until it fits.
            var query = BuildQuery(terms, segments, options.SynonymBoost);
            var dropped = 0;
            while (options.MaxBoostedQueryLength > 0 && query.Length > options.MaxBoostedQueryLength && query.IndexOfAny(BoostOrFuzzy) >= 0)
            {
                var largest = segments.Where(x => x.Synonyms.Count > 0).OrderByDescending(x => x.Synonyms.Count).ThenByDescending(x => x.Start).FirstOrDefault();
                if (largest == null)
                {
                    break;
                }

                largest.Synonyms.RemoveAt(largest.Synonyms.Count - 1);
                dropped++;
                query = BuildQuery(terms, segments, options.SynonymBoost);
            }

            var queriesForMatch = new List<string> { string.Join(" ", terms) };
            foreach (var segment in segments)
            {
                var before = string.Join(" ", terms.Take(segment.Start));
                var after = string.Join(" ", terms.Skip(segment.Start + segment.Length));
                foreach (var synonym in segment.Synonyms)
                {
                    queriesForMatch.Add(string.Join(" ", new[] { before, synonym, after }.Where(x => x.Length > 0)));
                }
            }

            var clauseCount = segments.Sum(x => x.Synonyms.Count > 0 ? 1 : x.Length);
            return new SynonymExpansion(terms, query, segments.Any(x => x.Synonyms.Count > 0),
                queriesForMatch.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), clauseCount, dropped);
        }

        private static readonly char[] BoostOrFuzzy = { '^', '~' };

        /// <summary>A matched phrase with its synonyms, or (no synonyms) the user's term(s) as they are.</summary>
        private sealed class Segment
        {
            public Segment(int start, int length, string phrase, List<string> synonyms)
            {
                Start = start;
                Length = length;
                Phrase = phrase;
                Synonyms = synonyms;
            }

            public int Start { get; }
            public int Length { get; }
            public string Phrase { get; }
            public List<string> Synonyms { get; }
        }

        private static string BuildQuery(string[] terms, List<Segment> segments, double synonymBoost)
            => string.Join(" ", segments.Select(x => x.Synonyms.Count > 0
                ? ExpandPhrase(x.Phrase, x.Synonyms, synonymBoost)
                : string.Join(" ", terms.Skip(x.Start).Take(x.Length).Select(EscapeTerm))));

        /// <summary>Longest phrase (in terms) in the synonym list; computed once per dictionary instance.</summary>
        internal static int GetLongestPhrase(IDictionary<string, HashSet<string>> synonyms)
        {
            return (int)LongestPhraseCache.GetValue(synonyms, d => d.Keys.Count == 0
                ? 0
                : d.Keys.Max(k => k.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length));
        }

        private static bool TryGetSynonyms(IDictionary<string, HashSet<string>> synonyms, string phrase, Func<string, string>? stem, string? languageKey, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out HashSet<string> matchingSynonyms)
        {
            if (synonyms.TryGetValue(phrase, out matchingSynonyms) || synonyms.TryGetValue(phrase.ToLowerInvariant(), out matchingSynonyms))
            {
                return true;
            }

            if (stem == null)
            {
                return false;
            }

            // By stem: notebooks => notebook, koppar => kopp. The synonyms themselves are added as written in the list.
            var stemmedIndex = SynonymStemming.StemmedIndex(synonyms, languageKey ?? string.Empty, stem);
            return stemmedIndex.TryGetValue(SynonymStemming.StemPhrase(phrase, stem), out matchingSynonyms);
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
            if (QueryText.IsQuoted(phrase))
            {
                phrase = phrase.Trim('"');
            }

            return Math.Max(1, phrase.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Count(t => t.Any(char.IsLetterOrDigit)));
        }

        private static string ToClause(string phrase)
        {
            if (!QueryText.IsQuoted(phrase) && ContainsMultipleTerms(phrase))
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
