using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Tracing;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using System;
using System.Linq;

namespace EPiServer.Labs.Find.Toolbox
{
    [ServiceConfiguration(Lifecycle = ServiceInstanceScope.Singleton)]
    public class UsingSynonymService
    {
        private readonly ISynonymProvider _synonymProvider;
        private readonly FindToolboxOptions _options;
        private static readonly ILogger log = LogManager.GetLogger(typeof(UsingSynonymService));

        public UsingSynonymService(ISynonymProvider synonymProvider, FindToolboxOptions options)
        {
            _synonymProvider = synonymProvider ?? throw new ArgumentNullException(nameof(synonymProvider));
            _options = options ?? FindToolboxOptions.Defaults;
        }

        public IQueriedSearch<TSource, QueryStringQuery> UsingSynonyms<TSource>(IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                if (context.RequestBody.Query == null)
                {
                    return;
                }

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out IQuery currentQuery, out BoolQuery currentBoolQuery))
                {
                    Trace.Instance.Add(new TraceEvent(search, "The use of synonyms are only supported for QueryStringQueries, i.e. with the use of the .For() -extensions. The query will be executed without the use of synonyms.") { IsError = false });
                    return;
                }

                var queryStringQuery = (QueryStringQuery)currentQuery;
                var rawQuery = QueryHelpers.GetRawQueryString(queryStringQuery);
                if (rawQuery.IsNullOrEmpty())
                {
                    return;
                }

                var terms = QueryHelpers.GetQueryPhrases(rawQuery, _options.MaxQueryTerms);
                if (terms.Length == 0)
                {
                    return;
                }

                var expansion = SynonymExpander.Expand(terms, _synonymProvider.GetSynonyms(cacheDuration), _options);

                // Without synonym matches the user's query is left untouched (including any operators),
                // we only record the match variations for UsingRelevanceImproved().
                var queryText = expansion.HasSynonyms ? expansion.ExpandedQuery : queryStringQuery.Query?.ToString();

                var newQuery = CreateQuery(queryText, queryStringQuery);
                newQuery.QueriesForMatch = expansion.QueriesForMatch.Take(Math.Max(1, _options.MaxQueriesForMatch)).ToArray();
                newQuery.IsSingleClause = expansion.HasSynonyms && expansion.ClauseCount == 1;

                ReplaceQuery(context, currentBoolQuery, newQuery);
                log.Debug("UsingSynonymsImproved: {0}", queryText);
            });
        }

        /// <summary>
        /// Creates the single query_string query that carries both the user's terms and the synonym groups.
        /// Keeping everything in ONE query is what makes MinimumShouldMatch and AND work together with synonyms:
        /// each synonym group is a single top level clause.
        /// </summary>
        internal static MinShouldMatchQueryStringQuery CreateQuery(string query, QueryStringQuery source)
        {
            var newQuery = new MinShouldMatchQueryStringQuery(query);
            QueryHelpers.CopySettings(source, newQuery);

            var minimumShouldMatch = (source as MinShouldMatchQueryStringQuery)?.MinimumShouldMatch;
            if (minimumShouldMatch.IsNotNullOrEmpty())
            {
                // MinimumShouldMatch() takes precedence over WithAndAsDefaultOperator(); it only works with OR.
                newQuery.MinimumShouldMatch = minimumShouldMatch;
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

        private static void ReplaceQuery(ISearchContext context, BoolQuery currentBoolQuery, IQuery newQuery)
        {
            if (currentBoolQuery != null && currentBoolQuery.Should.Count > 0 && ReferenceEquals(context.RequestBody.Query, currentBoolQuery))
            {
                currentBoolQuery.Should[0] = newQuery;
                return;
            }

            context.RequestBody.Query = newQuery;
        }
    }
}
