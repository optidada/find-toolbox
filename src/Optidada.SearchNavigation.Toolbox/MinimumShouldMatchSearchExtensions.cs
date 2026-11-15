using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Logging;
using Optidada.SearchNavigation.Toolbox.Queries;

namespace Optidada.SearchNavigation.Toolbox
{
    /// <summary><c>MinimumShouldMatch()</c>: how many of the query's terms must match.</summary>
    public static class MinimumShouldMatchSearchExtensions
    {
        private static readonly ILogger Log = LogManager.GetLogger(typeof(MinimumShouldMatchSearchExtensions));

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

                var query = ToolboxQueryStringQuery.CopyOf(main.Query, main.Query.Query?.ToString());
                query.MinimumShouldMatch = minMatch;
                query.DefaultOperator = BooleanOperator.Or;
                // One term (or one synonym group): nothing to count, and the analyzer may turn the single word into
                // several tokens (e.g. word + stem) that minimum_should_match would then all require.
                query.Toolbox.IsSingleClause = query.Toolbox.IsSingleClause || IsSingleTerm(main.RawQuery, options);

                main.Replace(query);
                Log.Debug("MinimumShouldMatch({0})", minMatch);
            });
        }

        private static bool IsSingleTerm(string rawQuery, ToolboxOptions options)
            => !string.IsNullOrWhiteSpace(rawQuery)
               && !QueryText.RespectsBooleanOperators(rawQuery, options)
               && QueryText.GetQueryPhrases(rawQuery, options.MaxQueryTerms).Length == 1;
    }
}
