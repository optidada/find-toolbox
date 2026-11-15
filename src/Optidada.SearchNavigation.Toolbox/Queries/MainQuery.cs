using System.Linq;
using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Tracing;
using Optidada.SearchNavigation.Toolbox.Synonyms;

namespace Optidada.SearchNavigation.Toolbox.Queries
{
    /// <summary>
    /// The main query of a search: the query_string query created by <c>For()</c>, possibly already rewritten by toolbox
    /// extensions and/or wrapped in a bool query that holds the optional relevance / fuzzy / wildcard clauses.
    /// Every toolbox extension starts from here, so they all find, replace and extend the main query the same way and can
    /// be called in any order after <c>For()</c>.
    /// </summary>
    internal sealed class MainQuery
    {
        private readonly ISearchContext _context;
        private BoolQuery? _wrapper;

        private MainQuery(ISearchContext context, QueryStringQuery query, BoolQuery? wrapper)
        {
            _context = context;
            _wrapper = wrapper;
            Query = query;
        }

        /// <summary>The current main query.</summary>
        public QueryStringQuery Query { get; private set; }

        /// <summary>What the user typed (what search statistics record); never changed by the toolbox.</summary>
        public string RawQuery => QueryText.RawQuery(Query);

        /// <summary>The toolbox state of the main query (empty for a plain For() query).</summary>
        public ToolboxQueryState State => (Query as ToolboxQueryStringQuery)?.Toolbox ?? new ToolboxQueryState();

        /// <summary>The minimum_should_match requested for the main query, if any.</summary>
        public string? MinimumShouldMatch => (Query as ToolboxQueryStringQuery)?.MinimumShouldMatch;

        /// <summary>
        /// Finds the main query, or returns null (and adds a trace message) when the search has no query_string query,
        /// i.e. the extension was called without <c>For()</c> first.
        /// </summary>
        public static MainQuery? Find(ISearch? search, ISearchContext context, string extension)
        {
            switch (context.RequestBody.Query)
            {
                case QueryStringQuery query:
                    return new MainQuery(context, query, null);
                case BoolQuery wrapper when wrapper.Should.FirstOrDefault() is QueryStringQuery query:
                    return new MainQuery(context, query, wrapper);
                default:
                    Trace.Instance.Add(new TraceEvent(search, extension + " relies on the query string query created by For(): call it after For(). The search runs without it.") { IsError = false });
                    return null;
            }
        }

        /// <summary>Replaces the main query, keeping its position (inside the wrapper bool query if there is one).</summary>
        public void Replace(QueryStringQuery query)
        {
            if (_wrapper != null)
            {
                _wrapper.Should[0] = query;
            }
            else
            {
                _context.RequestBody.Query = query;
            }

            Query = query;
        }

        /// <summary>Adds optional (should) clauses next to the main query, wrapping it in a bool query the first time.</summary>
        public void AddOptional(params IQuery[] clauses)
        {
            if (clauses.Length == 0)
            {
                return;
            }

            if (_wrapper == null)
            {
                _wrapper = new BoolQuery();
                _wrapper.Should.Add(Query);
                _context.RequestBody.Query = _wrapper;
            }

            foreach (var clause in clauses)
            {
                _wrapper.Should.Add(clause);
            }
        }

        /// <summary>
        /// Upper case AND / OR / NOT in the user's query. Respect: returns true - the caller leaves the query to Find.
        /// Ignore (default): removes them from the main query (once) and returns false.
        /// </summary>
        public bool LeaveToFind(ToolboxOptions options)
        {
            if (QueryText.RespectsBooleanOperators(RawQuery, options))
            {
                return true;
            }

            RemoveBooleanOperators(options);
            return false;
        }

        /// <summary>
        /// BooleanOperatorHandling.Ignore: replaces the main query (once) with the user's words without AND / OR / NOT, so
        /// Find does not treat them as operators. Decided on RawQuery (what the user typed): after synonym expansion the
        /// query text contains OR on purpose.
        /// </summary>
        public void RemoveBooleanOperators(ToolboxOptions options)
        {
            if (options.BooleanOperators != BooleanOperatorHandling.Ignore || State.BooleanOperatorsRemoved || !QueryText.HasBooleanOperators(RawQuery))
            {
                return;
            }

            var terms = QueryText.GetQueryPhrases(RawQuery, options.MaxQueryTerms);
            if (terms.Length == 0)
            {
                return;
            }

            var query = ToolboxQueryStringQuery.CopyOf(Query, string.Join(" ", terms.Select(SynonymExpander.EscapeTerm)));
            query.Toolbox.BooleanOperatorsRemoved = true;
            query.Toolbox.IsSingleClause = query.Toolbox.IsSingleClause || terms.Length == 1;
            Replace(query);
        }
    }
}
