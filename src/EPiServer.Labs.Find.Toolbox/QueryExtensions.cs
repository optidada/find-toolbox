using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Find.Json;
using Newtonsoft.Json;

namespace EPiServer.Labs.Find.Toolbox
{
    /// <summary>
    /// The query_string query the toolbox sends: Find's multi field query string query plus minimum_should_match,
    /// lenient, and the toolbox's own state (not sent).
    /// </summary>
    public class MinShouldMatchQueryStringQuery : MultiFieldQueryStringQuery
    {
        public MinShouldMatchQueryStringQuery(FieldFilterValue query)
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

        [JsonProperty("lenient", NullValueHandling = NullValueHandling.Ignore)]
        public bool Lenient { get; set; } = true;

    }

    public class MinShouldMatchBoolQuery : BoolQuery
    {
        public MinShouldMatchBoolQuery() : base()
        {
        }

        [JsonProperty("minimum_should_match", NullValueHandling = NullValueHandling.Ignore)]
        public string? MinimumShouldMatch { get; set; }

    }

    /// <summary>match_phrase: the field contains the words in this order (with Slop words in between).</summary>
    [JsonConverter(typeof(PhraseQueryConverter))]
    public class PhraseQuery : BoostableQuery
    {
        public PhraseQuery(string field, string value)
        {
            Field = field ?? throw new System.ArgumentNullException(nameof(field));
            Value = value ?? throw new System.ArgumentNullException(nameof(value));
        }

        [JsonIgnore]
        public string Field { get; set; }

        [JsonProperty("query")]
        public string Value { get; set; }

        /// <summary>How many words may be between / around the phrase words (FindToolboxOptions.PhraseSlop, default 2).</summary>
        [JsonProperty("slop")]
        public int Slop { get; set; } = 2;

    }

    public class PhraseQueryConverter : CustomWriteConverterBase<PhraseQuery>
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value.IsNot<PhraseQuery>())
            {
                writer.WriteNull();
                return;
            }
            var query = (PhraseQuery)value;

            writer.WriteStartObject();
            writer.WritePropertyName("match_phrase");
            writer.WriteStartObject();
            writer.WritePropertyName(query.Field);
            writer.WriteStartObject();
            WriteNonIgnoredProperties(writer, value, serializer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }

    /// <summary>match_phrase_prefix: the field contains the phrase, the last word as a prefix.</summary>
    [JsonConverter(typeof(PhrasePrefixQueryConverter))]
    public class PhrasePrefixQuery : BoostableQuery
    {
        public PhrasePrefixQuery(string field, string value)
        {
            Field = field ?? throw new System.ArgumentNullException(nameof(field));
            Value = value ?? throw new System.ArgumentNullException(nameof(value));
        }

        [JsonIgnore]
        public string Field { get; set; }

        [JsonProperty("query")]
        public string Value { get; set; }
    }

    public class PhrasePrefixQueryConverter : CustomWriteConverterBase<PhrasePrefixQuery>
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value.IsNot<PhrasePrefixQuery>())
            {
                writer.WriteNull();
                return;
            }
            var query = (PhrasePrefixQuery)value;
            writer.WriteStartObject();
            writer.WritePropertyName("match_phrase_prefix");
            writer.WriteStartObject();
            writer.WritePropertyName(query.Field);
            writer.WriteStartObject();
            WriteNonIgnoredProperties(writer, value, serializer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }

}