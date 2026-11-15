using System;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using Newtonsoft.Json;

namespace Optidada.SearchNavigation.Toolbox.Queries
{
    /// <summary>match_phrase_prefix: the field contains the phrase, the last word as a prefix.</summary>
    [JsonConverter(typeof(FieldQueryConverter<PhrasePrefixQuery>))]
    public class PhrasePrefixQuery : BoostableQuery, IFieldQuery
    {
        /// <summary>A query for <paramref name="value"/> in <paramref name="field"/>.</summary>
        public PhrasePrefixQuery(string field, string value)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The field name (as indexed, e.g. <c>Title$$string.standard</c>).</summary>
        [JsonIgnore]
        public string Field { get; set; }

        /// <summary>The text to match.</summary>
        [JsonProperty("query")]
        public string Value { get; set; }

        string IFieldQuery.QueryType => "match_phrase_prefix";
    }
}
