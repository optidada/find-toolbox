using System;
using System.Collections.Generic;
using System.Linq;
using EPiServer.Find;
using EPiServer.Find.Json;
using Newtonsoft.Json.Linq;
using Optidada.SearchNavigation.Toolbox.Queries;
using Optidada.SearchNavigation.Toolbox.Synonyms;

namespace Optidada.SearchNavigation.Toolbox.UnitTests
{
    public class Doc
    {
        public string Title { get; set; }
        public string Body { get; set; }
    }

    /// <summary>Fixed, in-memory synonyms so tests never call the Find service.</summary>
    public class FakeSynonymProvider : ISynonymProvider
    {
        private readonly Dictionary<string, HashSet<string>> _synonyms;

        public FakeSynonymProvider(params (string phrase, string synonym, bool bidirectional)[] synonyms)
        {
            _synonyms = FindSynonymProvider.CreateSynonymDictionary(synonyms.Select(s => TestSynonyms.Create(s.phrase, s.synonym, s.bidirectional)));
        }

        public Dictionary<string, HashSet<string>> GetSynonyms(TimeSpan? cacheDuration = null) => _synonyms;
    }

    public static class TestSearch
    {
        private static readonly Lazy<IClient> _client = new Lazy<IClient>(CreateClient);

        /// <summary>An offline client: it is only used to build requests, never to send them.</summary>
        public static IClient Client => _client.Value;

        private static IClient CreateClient()
        {
#if NETFRAMEWORK
            return new Client("http://localhost:65535/", "unit-tests");
#else
            var options = Microsoft.Extensions.Options.Options.Create(new FindOptions
            {
                ServiceUrl = "http://localhost:65535/",
                DefaultIndex = "unit-tests"
            });
            var cache = new EPiServer.Find.Connection.RuntimeCacheAdapter(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient(services);
            var httpClientFactory = (System.Net.Http.IHttpClientFactory)Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
                .BuildServiceProvider(services).GetService(typeof(System.Net.Http.IHttpClientFactory));
            return new Client(options, cache, httpClientFactory);
#endif
        }

        public static IQueriedSearch<Doc, EPiServer.Find.Api.Querying.Queries.QueryStringQuery> For(string query) => Client.Search<Doc>().For(query);

        public static SynonymService SynonymServiceWith(params (string phrase, string synonym, bool bidirectional)[] synonyms)
            => new SynonymService(new FakeSynonymProvider(synonyms), Unboosted);

        /// <summary>Options without synonym down-weighting, so structural tests read naturally.</summary>
        public static ToolboxOptions Unboosted => new ToolboxOptions { SynonymBoost = 1, SynonymVariationBoostFactor = 1 };

        /// <summary>Applies all search actions and returns the "query" part of the request body as JSON.</summary>
        public static JObject QueryJson(ISearch search)
        {
            var context = new SearchContext();
            search.ApplyActions(context);
            var json = Serializer.Serialize(Serializer.CreateDefault(), context.RequestBody);
            return (JObject)JObject.Parse(json)["query"];
        }

        /// <summary>All query_string objects in the request, in document order.</summary>
        public static List<JObject> QueryStrings(JToken query)
            => query.SelectTokens("$..query_string").Cast<JObject>().ToList();
    }
}
