using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers.Text;
using EPiServer.Logging;
using Optidada.SearchNavigation.Toolbox.Queries;

namespace Optidada.SearchNavigation.Toolbox
{
    /// <summary><c>FuzzyMatch()</c> and <c>WildcardMatch()</c>: words with typos and partial words.</summary>
    public static class FuzzyWildcardSearchExtensions
    {
        private static readonly ILogger Log = LogManager.GetLogger(typeof(FuzzyWildcardSearchExtensions));
        private static readonly char[] BoostOrFuzzy = { '^', '~' };

        /// <summary>Finds words with typos (default boost <see cref="ToolboxOptions.FuzzyBoost"/>).</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
            => search.FuzzyMatch(ToolboxServices.Options.FuzzyBoost, fieldSelectors);

        /// <summary>Finds words with typos.</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, double boost, params Expression<Func<TSource, string>>[] fieldSelectors)
            => search.TermVariantMatch("FuzzyMatch()", fieldSelectors, boost,
                term => term.Quote() + "~",
                options => options.FuzzyUseLanguageField,
                (query, options) => query.FuzzyPrefixLength = options.FuzzyPrefixLength);

        /// <summary>Finds partial words, right side wildcard (default boost <see cref="ToolboxOptions.WildcardBoost"/>).</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
            => search.WildcardMatch(ToolboxServices.Options.WildcardBoost, doubleSided: false, fieldSelectors);

        /// <summary>Finds partial words. <paramref name="doubleSided"/> adds a leading wildcard too: 15-40x slower (see README).</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, double boost, bool doubleSided, params Expression<Func<TSource, string>>[] fieldSelectors)
            => search.TermVariantMatch("WildcardMatch()", fieldSelectors, boost,
                term => doubleSided ? "*" + term.Quote() + "*" : term.Quote() + "*",
                options => options.WildcardUseLanguageField,
                configure: null);

        /// <summary>
        /// An optional query_string with a variant (term~ / term*) of the user's words, on the given fields, with the main
        /// query's operator and minimum_should_match.
        /// </summary>
        private static IQueriedSearch<TSource, QueryStringQuery> TermVariantMatch<TSource>(this IQueriedSearch<TSource> search, string extension,
            Expression<Func<TSource, string>>[] fieldSelectors, double boost, Func<string, string> variant,
            Func<ToolboxOptions, bool> useLanguageField, Action<ToolboxQueryStringQuery, ToolboxOptions>? configure)
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
                if (string.IsNullOrEmpty(rawQuery) || QueryText.IsQuoted(rawQuery) || QueryText.IsParenthesized(rawQuery))
                {
                    return;
                }

                var candidates = QueryText.GetQueryPhrases(rawQuery, options.MaxQueryTerms)
                    .Where(x => !QueryText.IsQuoted(x)
                                && x.Length >= options.MinFuzzyWildcardTermLength
                                && x.Length <= options.MaxFuzzyWildcardTermLength)
                    .Take(Math.Max(0, options.MaxFuzzyWildcardTerms))
                    .Select(variant)
                    .ToList();

                // The Find service rejects fuzzy (~) query_string queries longer than MaxBoostedQueryLength (400)
                while (candidates.Count > 0 && IsTooLong(string.Join(" ", candidates), options))
                {
                    candidates.RemoveAt(candidates.Count - 1);
                }

                if (candidates.Count == 0)
                {
                    return;
                }

                var query = new ToolboxQueryStringQuery(string.Join(" ", candidates))
                {
                    Fields = SearchFieldNames(search.Client, context, fieldSelectors, useLanguageField(options)),
                    DefaultOperator = main.Query.DefaultOperator,
                    MinimumShouldMatch = main.MinimumShouldMatch,
                    Boost = boost,
                };
                configure?.Invoke(query, options);

                main.AddOptional(query);
                Log.Debug("{0}: {1}", extension, query.Query);
            });
        }

        private static bool IsTooLong(string query, ToolboxOptions options)
            => options.MaxBoostedQueryLength > 0 && query.Length > options.MaxBoostedQueryLength && query.IndexOfAny(BoostOrFuzzy) >= 0;

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
    }
}
