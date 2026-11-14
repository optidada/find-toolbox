using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using EPiServer.Find.Json;
using Newtonsoft.Json;

namespace EPiServer.Labs.Find.Toolbox
{
    public class MinShouldMatchQueryStringQuery : MultiFieldQueryStringQuery
    {
        public MinShouldMatchQueryStringQuery(FieldFilterValue query)
            : base(query)
        {
        }

        /// <summary>The requested minimum_should_match (inherited by FuzzyMatch/WildcardMatch).</summary>
        [JsonIgnore]
        public string MinimumShouldMatch { get; set; }

        /// <summary>
        /// True when the whole query is ONE synonym group, e.g. <c>(find OR (search AND navigation))</c>.
        /// The query parser unwraps the outer parentheses, which would make minimum_should_match count the
        /// alternatives (100% => find AND search AND navigation). With a single clause there is nothing to count,
        /// so minimum_should_match is not sent.
        /// </summary>
        [JsonIgnore]
        public bool IsSingleClause { get; set; }

        [JsonProperty("minimum_should_match", NullValueHandling = NullValueHandling.Ignore)]
        public string MinimumShouldMatchForRequest => IsSingleClause ? null : MinimumShouldMatch;

        [JsonProperty("lenient", NullValueHandling = NullValueHandling.Ignore)]
        public bool Lenient { get; set; } = true;

        [JsonIgnore]
        public string[] QueriesForMatch { get; set; }

    }

    public class MinShouldMatchBoolQuery : BoolQuery
    {
        public MinShouldMatchBoolQuery() : base()
        {
        }

        [JsonProperty("minimum_should_match", NullValueHandling = NullValueHandling.Ignore)]
        public string MinimumShouldMatch { get; set; }

    }

    [JsonConverter(typeof(PhraseQueryConverter))]
    public class PhraseQuery : BoostableQuery
    {
        public PhraseQuery(string field, string value)
        {
            //TODO: Validate args not null
            Field = field;
            Value = value;
        }

        [JsonIgnore]
        public string Field { get; set; }

        [JsonProperty("query")]
        public string Value { get; set; }

        [JsonProperty("slop")]
        public int Slop { get { return 2; } }

    }

    public class PhraseQueryConverter : CustomWriteConverterBase<PrefixQuery>
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

    [JsonConverter(typeof(PhrasePrefixQueryConverter))]
    public class PhrasePrefixQuery : BoostableQuery
    {
        public PhrasePrefixQuery(string field, string value)
        {
            //TODO: Validate args not null
            Field = field;
            Value = value;
        }

        [JsonIgnore]
        public string Field { get; set; }

        [JsonProperty("query")]
        public string Value { get; set; }
    }

    public class PhrasePrefixQueryConverter : CustomWriteConverterBase<PrefixQuery>
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