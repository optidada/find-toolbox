using System;
using System.Linq;
using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers.Text;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using Optidada.SearchNavigation.Toolbox.Queries;

namespace Optidada.SearchNavigation.Toolbox.Synonyms
{
    /// <summary>
    /// UsingSynonymsImproved(): expands the main query with the synonyms of an <see cref="ISynonymProvider"/>, inside the one
    /// query_string query, so a term and its synonyms count as one clause for MinimumShouldMatch and AND.
    /// </summary>
    [ServiceConfiguration(Lifecycle = ServiceInstanceScope.Singleton)]
    public class SynonymService
    {
        private readonly ISynonymProvider _synonymProvider;
        private readonly ToolboxOptions _options;
        private static readonly ILogger Log = LogManager.GetLogger(typeof(SynonymService));

        /// <summary>A service with the given synonym source (options: defaults when null).</summary>
        public SynonymService(ISynonymProvider synonymProvider, ToolboxOptions? options)
        {
            _synonymProvider = synonymProvider ?? throw new ArgumentNullException(nameof(synonymProvider));
            _options = options ?? ToolboxOptions.Defaults;
        }

        /// <summary>The search with its main query expanded with synonyms (what UsingSynonymsImproved() calls).</summary>
        public IQueriedSearch<TSource, QueryStringQuery> UsingSynonyms<TSource>(IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                var main = MainQuery.Find(search, context, "UsingSynonymsImproved()");
                if (main == null || main.RawQuery.IsNullOrEmpty())
                {
                    return;
                }

                // AND / OR / NOT: respected => leave the query exactly as Find interprets it; ignored (default) => removed
                if (main.LeaveToFind(_options))
                {
                    Log.Debug("UsingSynonymsImproved: query uses boolean operators, not expanded: {0}", main.RawQuery);
                    return;
                }

                var terms = QueryText.GetQueryPhrases(main.RawQuery, _options.MaxQueryTerms);
                if (terms.Length == 0)
                {
                    return;
                }

                var expansion = SynonymExpander.Expand(terms, _synonymProvider.GetSynonyms(cacheDuration), _options,
                    stem: _options.StemSynonymLookup ? SynonymStemming.For(context.ContentLanguage) : null,
                    languageKey: context.ContentLanguage?.FieldSuffix);

                // Without synonym matches the user's query is kept as it is; the match variations are still recorded for
                // UsingRelevanceImproved().
                var queryText = expansion.HasSynonyms ? expansion.ExpandedQuery : main.Query.Query?.ToString();

                var newQuery = CreateQuery(queryText, main.Query);
                newQuery.Toolbox.QueriesForMatch = expansion.QueriesForMatch.Take(Math.Max(1, _options.MaxQueriesForMatch)).ToArray();
                // One top level clause (one word or one synonym group): minimum_should_match has nothing to count
                newQuery.Toolbox.IsSingleClause = expansion.ClauseCount == 1;

                main.Replace(newQuery);
                Log.Debug("UsingSynonymsImproved: {0}", queryText);
            });
        }

        /// <summary>
        /// Creates the single query_string query that carries both the user's terms and the synonym groups.
        /// Keeping everything in ONE query is what makes MinimumShouldMatch and AND work together with synonyms:
        /// each synonym group is a single top level clause.
        /// </summary>
        internal static ToolboxQueryStringQuery CreateQuery(string? query, QueryStringQuery source)
        {
            var newQuery = ToolboxQueryStringQuery.CopyOf(source, query);

            if (newQuery.MinimumShouldMatch.IsNotNullOrEmpty())
            {
                // MinimumShouldMatch() takes precedence over WithAndAsDefaultOperator(); it only works with OR.
                newQuery.DefaultOperator = BooleanOperator.Or;
            }
            else
            {
                // Keep AND (WithAndAsDefaultOperator) or OR. Synonym groups use explicit OR inside their parentheses.
                newQuery.MinimumShouldMatch = null;
                newQuery.DefaultOperator = source.DefaultOperator;
            }

            return newQuery;
        }
    }
}
