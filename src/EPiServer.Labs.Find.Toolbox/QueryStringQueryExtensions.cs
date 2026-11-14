using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;

namespace EPiServer.Labs.Find.Toolbox
{
    /// <summary>
    /// The Find Toolbox extensions for searches created with <c>For()</c>. They all work on the main query
    /// (<see cref="MainQuery"/>) and can be combined in any order after <c>For()</c>.
    /// </summary>
    public static class QueryStringQueryExtensions
    {
        private static readonly ILogger log = LogManager.GetLogger(typeof(QueryStringQueryExtensions));

        /// <summary>Expands the query with the synonyms from the Find UI (see README).</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
            => search.UsingSynonymsImproved(ToolboxServices.SynonymService, cacheDuration);

        /// <summary>Expands the query with synonyms using an explicitly given service (e.g. with your own <see cref="ISynonymProvider"/>).</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search, UsingSynonymService usingSynonymService, TimeSpan? cacheDuration = null)
            => usingSynonymService.UsingSynonyms(search, cacheDuration);

        /// <summary>
        /// How many of the query's terms must match (e.g. "2", "100%", "2&lt;-25%"). A synonym group counts as one term;
        /// a query with one term (or one synonym group) sends no minimum_should_match - there is nothing to count.
        /// </summary>
        public static IQueriedSearch<TSource, QueryStringQuery> MinimumShouldMatch<TSource>(this IQueriedSearch<TSource> search, string minMatch)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                var main = MainQuery.Find(search, context, "MinimumShouldMatch()");
                if (main == null)
                {
                    return;
                }

                var options = ToolboxServices.Options;
                main.RemoveBooleanOperators(options);

                var query = QueryHelpers.CreateToolboxQuery(main.Query.Query?.ToString(), main.Query);
                query.MinimumShouldMatch = minMatch;
                query.DefaultOperator = BooleanOperator.Or;
                // One term (or one synonym group): nothing to count, and the analyzer may turn the single word into
                // several tokens (e.g. word + stem) that minimum_should_match would then all require.
                query.Toolbox.IsSingleClause = query.Toolbox.IsSingleClause || IsSingleTerm(main.RawQuery, options);

                main.Replace(query);
                log.Debug("MinimumShouldMatch({0})", minMatch);
            });
        }

        /// <summary>
        /// Boosts documents where the fields contain the query as a phrase (match_phrase), start with it
        /// (match_phrase_prefix) or, for one word, contain / start with the word. Also for the synonym variations from
        /// UsingSynonymsImproved(), with a lower boost.
        /// </summary>
        public static IQueriedSearch<TSource, QueryStringQuery> UsingRelevanceImproved<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                var main = MainQuery.Find(search, context, "UsingRelevanceImproved()");
                var options = ToolboxServices.Options;
                if (main == null || main.LeaveToFind(options))
                {
                    return;
                }

                // The user's words, or (after UsingSynonymsImproved) the user's words + synonym variations
                var queriesForMatch = main.State.QueriesForMatch is string[] variations && variations.Length > 0
                    ? variations
                    : new[] { string.Join(" ", QueryHelpers.GetQueryPhrases(main.RawQuery, options.MaxQueryTerms)) };

                var clauses = new List<IQuery>();
                var originalTermCount = 1;
                for (var variation = 0; variation < queriesForMatch.Length; variation++)
                {
                    // match_phrase / match_phrase_prefix / prefix are not query_string queries: no escaping, no quotes.
                    // Punctuation only tokens (e.g. & in "search & navigation") are dropped: Find's shingled fields index them
                    // as tokens, which inflates phrase scores. Limit the number of terms: match_phrase_prefix expands the last term.
                    var terms = queriesForMatch[variation].Replace("\"", string.Empty)
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Any(char.IsLetterOrDigit))
                        .Take(Math.Max(1, options.MaxRelevanceTerms))
                        .ToArray();
                    if (terms.Length == 0)
                    {
                        continue;
                    }

                    // The first variation is the user's query; the rest are synonym variations. Synonym variations get a lower
                    // boost, normalized by length: phrase scores grow with the number of (shingled) terms, so a longer synonym
                    // would otherwise outrank the user's own words.
                    if (variation == 0)
                    {
                        originalTermCount = terms.Length;
                    }
                    var factor = variation == 0 ? 1 : options.SynonymVariationBoostFactor * Math.Min(1.0, (double)originalTermCount / terms.Length);

                    clauses.AddRange(RelevanceClauses(search.Client, fieldSelectors, terms, factor, options));
                }

                main.AddOptional(clauses.ToArray());
                log.Debug("UsingRelevanceImproved: {0} clauses", clauses.Count);
            });
        }

        /// <summary>Finds words with typos (default boost <see cref="FindToolboxOptions.FuzzyBoost"/>).</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
            => search.FuzzyMatch(ToolboxServices.Options.FuzzyBoost, fieldSelectors);

        /// <summary>Finds words with typos.</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, double boost, params Expression<Func<TSource, string>>[] fieldSelectors)
            => search.TermVariantMatch("FuzzyMatch()", fieldSelectors, boost,
                term => term.Quote() + "~",
                options => options.FuzzyUseLanguageField,
                (query, options) => query.FuzzyPrefixLength = options.FuzzyPrefixLength);

        /// <summary>Finds partial words, right side wildcard (default boost <see cref="FindToolboxOptions.WildcardBoost"/>).</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
            => search.WildcardMatch(ToolboxServices.Options.WildcardBoost, doubleSided: false, fieldSelectors);

        /// <summary>Finds partial words. <paramref name="doubleSided"/> adds a leading wildcard too: very expensive.</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, double boost, bool doubleSided, params Expression<Func<TSource, string>>[] fieldSelectors)
            => search.TermVariantMatch("WildcardMatch()", fieldSelectors, boost,
                term => doubleSided ? "*" + term.Quote() + "*" : term.Quote() + "*",
                options => options.WildcardUseLanguageField,
                configure: null);

        /// <summary>
        /// Adds a field to the query (like Find's InField) in both the language neutral and the language field. Works in any
        /// order with the other toolbox extensions.
        /// </summary>
        public static IQueriedSearch<TSource, QueryStringQuery> InFieldImproved<TSource, TExistingQuery>(
                this IQueriedSearch<TSource, TExistingQuery> search,
                Expression<Func<TSource, string>> fieldSelector,
                double? relativeImportance = null)
                where TExistingQuery : QueryStringQuery
        {
            fieldSelector.ValidateNotNullArgument("fieldSelector");

            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                var nonLanguageField = search.Client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector);
                var languageField = search.Client.Conventions.FieldNameConvention.GetFieldNameForSearch(fieldSelector, context.ContentLanguage);

                AddFieldToQueryStringQuery(context, nonLanguageField, relativeImportance);
                AddFieldToQueryStringQuery(context, languageField, relativeImportance);
            });
        }

        /// <summary>Adds a field (with optional boost) to the main query, also when other toolbox extensions wrapped it.</summary>
        public static void AddFieldToQueryStringQuery(ISearchContext context, string fieldNameToAdd, double? relativeImportance)
        {
            var main = MainQuery.Find(null, context, "InFieldImproved()");
            if (main == null)
            {
                return;
            }

            if (!(main.Query is MultiFieldQueryStringQuery query))
            {
                query = new MultiFieldQueryStringQuery(main.Query.Query);
                QueryHelpers.CopySettings(main.Query, query);
                if (main.Query.DefaultField.IsNotNull())
                {
                    query.Fields.Add(main.Query.DefaultField);
                }
                main.Replace(query);
            }

            if (relativeImportance != null)
            {
                // Invariant culture: "0,5" (e.g. on a Swedish server) is not valid query syntax
                fieldNameToAdd = fieldNameToAdd + "^" + relativeImportance.Value.ToString(CultureInfo.InvariantCulture);
            }

            query.Fields.Add(fieldNameToAdd);
        }

        /// <summary>
        /// FuzzyMatch and WildcardMatch: an optional query_string with a variant (term~ / term*) of the user's words, on the
        /// given fields, with the main query's operator and minimum_should_match.
        /// </summary>
        private static IQueriedSearch<TSource, QueryStringQuery> TermVariantMatch<TSource>(this IQueriedSearch<TSource> search, string extension,
            Expression<Func<TSource, string>>[] fieldSelectors, double boost, Func<string, string> variant,
            Func<FindToolboxOptions, bool> useLanguageField, Action<MinShouldMatchQueryStringQuery, FindToolboxOptions>? configure)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                var main = MainQuery.Find(search, context, extension);
                var options = ToolboxServices.Options;
                if (main == null || main.LeaveToFind(options))
                {
                    return;
                }

                // Not on quoted searches (exact phrase wanted)
                var rawQuery = main.RawQuery;
                if (string.IsNullOrEmpty(rawQuery) || QueryHelpers.IsStringQuoted(rawQuery) || QueryHelpers.IsStringParenthesized(rawQuery))
                {
                    return;
                }

                var candidates = QueryHelpers.GetQueryPhrases(rawQuery, options.MaxQueryTerms)
                    .Where(x => !QueryHelpers.IsStringQuoted(x)
                                && x.Length >= options.MinFuzzyWildcardTermLength
                                && x.Length <= options.MaxFuzzyWildcardTermLength)
                    .Take(Math.Max(0, options.MaxFuzzyWildcardTerms))
                    .Select(variant)
                    .ToList();
                // The Find service rejects fuzzy (~) query_string queries longer than MaxBoostedQueryLength (400)
                while (options.MaxBoostedQueryLength > 0 && candidates.Count > 0
                       && string.Join(" ", candidates).Length > options.MaxBoostedQueryLength && string.Join(" ", candidates).IndexOfAny(new[] { '^', '~' }) >= 0)
                {
                    candidates.RemoveAt(candidates.Count - 1);
                }
                if (candidates.Count == 0)
                {
                    return;
                }

                var query = new MinShouldMatchQueryStringQuery(string.Join(" ", candidates))
                {
                    Fields = SearchFieldNames(search.Client, context, fieldSelectors, useLanguageField(options)),
                    DefaultOperator = main.Query.DefaultOperator,
                    MinimumShouldMatch = main.MinimumShouldMatch,
                    Boost = boost,
                };
                configure?.Invoke(query, options);

                main.AddOptional(query);
                log.Debug("{0}: {1}", extension, query.Query);
            });
        }

        private static IEnumerable<IQuery> RelevanceClauses<TSource>(IClient client, IEnumerable<Expression<Func<TSource, string>>> fieldSelectors,
            string[] terms, double factor, FindToolboxOptions options)
        {
            var query = string.Join(" ", terms);
            foreach (var fieldSelector in fieldSelectors)
            {
                var lowercaseField = client.Conventions.FieldNameConvention.GetFieldNameForLowercase(fieldSelector);
                var analyzedField = client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector);

                if (terms.Length == 1)
                {
                    // Field contains the term
                    if (terms[0].Length > 1)
                    {
                        yield return new PhraseQuery(analyzedField, query) { Boost = options.PhraseBoost * factor, Slop = options.PhraseSlop };
                    }

                    // Field starts with the term
                    if (terms[0].Length >= options.MinPrefixLength)
                    {
                        yield return new PrefixQuery(lowercaseField, query.ToLowerInvariant()) { Boost = options.PrefixBoost * factor };
                    }
                }
                else if (terms.Any(x => x.Length > 1))
                {
                    // Field contains the phrase / field starts with the phrase
                    yield return new PhraseQuery(analyzedField, query) { Boost = options.PhraseBoost * factor, Slop = options.PhraseSlop };
                    yield return new PhrasePrefixQuery(lowercaseField, query.ToLowerInvariant()) { Boost = options.PhrasePrefixBoost * factor };
                }
            }
        }

        /// <summary>
        /// The language neutral field (.standard) and, when the search has a language, the language field (.en, .sv, ...)
        /// whose analyzer stems and (for compounding languages) decompounds at index time - the same pair InField() uses.
        /// </summary>
        private static List<string> SearchFieldNames<TSource>(IClient client, ISearchContext context, IEnumerable<Expression<Func<TSource, string>>> fieldSelectors, bool useLanguageField)
        {
            var fieldNames = new List<string>();
            foreach (var fieldSelector in fieldSelectors)
            {
                if (useLanguageField && context.ContentLanguage != null && context.ContentLanguage != Language.None)
                {
                    fieldNames.Add(client.Conventions.FieldNameConvention.GetFieldNameForSearch(fieldSelector, context.ContentLanguage));
                }

                fieldNames.Add(client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector));
            }

            return fieldNames.Distinct().ToList();
        }

        private static bool IsSingleTerm(string rawQuery, FindToolboxOptions options)
            => !string.IsNullOrWhiteSpace(rawQuery)
               && !QueryHelpers.RespectsBooleanOperators(rawQuery, options)
               && QueryHelpers.GetQueryPhrases(rawQuery, options.MaxQueryTerms).Length == 1;
    }
}
