using System.Linq;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using Newtonsoft.Json;

namespace Optidada.SearchNavigation.Toolbox.Queries
{
    /// <summary>
    /// The query_string query the toolbox sends: Find's multi field query string query plus minimum_should_match,
    /// lenient, and the toolbox's own state (not sent).
    /// </summary>
    public class ToolboxQueryStringQuery : MultiFieldQueryStringQuery
    {
        /// <summary>A query string query for <paramref name="query"/> (query string syntax).</summary>
        public ToolboxQueryStringQuery(FieldFilterValue? query)
            : base(query)
        {
        }

        /// <summary>The requested minimum_should_match (inherited by FuzzyMatch/WildcardMatch).</summary>
        [JsonIgnore]
        public string? MinimumShouldMatch { get; set; }

        /// <summary>What the toolbox extensions pass on to each other about this query (not sent to Find).</summary>
        [JsonIgnore]
        public ToolboxQueryState Toolbox { get; set; } = new ToolboxQueryState();

        /// <summary>minimum_should_match as sent: not for a single clause (see <see cref="ToolboxQueryState.IsSingleClause"/>).</summary>
        [JsonProperty("minimum_should_match", NullValueHandling = NullValueHandling.Ignore)]
        public string? MinimumShouldMatchForRequest => Toolbox.IsSingleClause ? null : MinimumShouldMatch;

        /// <summary>Ignore format errors (e.g. text in a numeric field) instead of failing the search.</summary>
        [JsonProperty("lenient", NullValueHandling = NullValueHandling.Ignore)]
        public bool Lenient { get; set; } = true;

        /// <summary>A toolbox query with <paramref name="query"/> as text and all settings and state of <paramref name="source"/>.</summary>
        internal static ToolboxQueryStringQuery CopyOf(QueryStringQuery source, string? query)
        {
            var target = new ToolboxQueryStringQuery(query)
            {
                RawQuery = source.RawQuery,
                AllowLeadingWildcard = source.AllowLeadingWildcard,
                AnalyzeWildcard = source.AnalyzeWildcard,
                Analyzer = source.Analyzer,
                AutoGeneratePhraseQueries = source.AutoGeneratePhraseQueries,
                Boost = source.Boost,
                DefaultOperator = source.DefaultOperator,
                EnablePositionIncrements = source.EnablePositionIncrements,
                FuzzyMinSim = source.FuzzyMinSim,
                FuzzyPrefixLength = source.FuzzyPrefixLength,
                LowercaseExpandedTerms = source.LowercaseExpandedTerms,
                PhraseSlop = source.PhraseSlop,
                DefaultField = source.DefaultField,
            };

            if (source is MultiFieldQueryStringQuery multiField && multiField.Fields != null)
            {
                target.Fields = multiField.Fields.ToList();
            }

            if (source is ToolboxQueryStringQuery toolbox)
            {
                target.MinimumShouldMatch = toolbox.MinimumShouldMatch;
                target.Lenient = toolbox.Lenient;
                target.Toolbox = toolbox.Toolbox.Clone();
            }

            return target;
        }
    }
}
