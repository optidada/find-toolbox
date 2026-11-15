using EPiServer.Find.Json;
using Newtonsoft.Json;

namespace Optidada.SearchNavigation.Toolbox.Queries
{
    /// <summary>A query on one field: <c>{ "match_phrase": { "Field": { ...properties } } }</c>.</summary>
    internal interface IFieldQuery
    {
        string Field { get; }

        string QueryType { get; }
    }

    /// <summary>Writes an <see cref="IFieldQuery"/> in the Elasticsearch field query format.</summary>
    internal sealed class FieldQueryConverter<TQuery> : CustomWriteConverterBase<TQuery>
        where TQuery : class, IFieldQuery
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (!(value is TQuery query))
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName(query.QueryType);
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
