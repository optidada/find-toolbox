using System;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using Newtonsoft.Json;

namespace Optidada.SearchNavigation.Toolbox.Queries
{
    /// <summary>match_phrase: the field contains the words in this order (with <see cref="Slop"/> words in between).</summary>
    [JsonConverter(typeof(FieldQueryConverter<PhraseQuery>))]
    public class PhraseQuery : BoostableQuery, IFieldQuery
    {
        /// <summary>A query for <paramref name="value"/> in <paramref name="field"/>.</summary>
        public PhraseQuery(string field, string value)
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

        /// <summary>How many words may be between / around the phrase words (<see cref="ToolboxOptions.PhraseSlop"/>, default 2).</summary>
        [JsonProperty("slop")]
        public int Slop { get; set; } = 2;

        string IFieldQuery.QueryType => "match_phrase";
    }
}
