using EPiServer;
using EPiServer.Core;
using EPiServer.DataAccess;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Cms;
using EPiServer.Find.Framework;
using EPiServer.Find.Json;
using EPiServer.Find.Optimizations;
using EPiServer.Find.Optimizations.Synonyms.Api;
using EPiServer.Labs.Find.Toolbox;
using EPiServer.Security;
using EPiServer.Web;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using EPiServer.Find.UnifiedSearch;
#if NETFRAMEWORK
using AlloyPage = AlloyCms11.Models.Pages.ArticlePage;
using AlloyContainer = AlloyCms11.Models.Pages.ContainerPage;
#else
using AlloyPage = AlloyCms12.Models.Pages.ArticlePage;
using AlloyContainer = AlloyCms12.Models.Pages.ContainerPage;
#endif

namespace ToolboxTestHarness
{
    /// <summary>
    /// Seeds <see cref="TestDataset"/> into the site and runs toolbox searches scoped to the seeded pages.
    /// Only for local test sites; never deploy this.
    /// </summary>
    public class ToolboxTestService
    {
        private readonly IContentRepository _contentRepository;
        private readonly IClient _client;

        public ToolboxTestService(IContentRepository contentRepository, IClient client)
        {
            _contentRepository = contentRepository;
            _client = client;
        }

        private static CultureInfo Culture => CultureInfo.GetCultureInfo(TestDataset.Language);

        public string SiteName =>
#if NETFRAMEWORK
            "cms11";
#else
            "cms12";
#endif

        /// <summary>
        /// (Re)creates the test container with one ArticlePage per test document, indexes them
        /// and (re)creates the test synonyms. Only touches the test container and the test synonym phrases.
        /// </summary>
        public async Task<object> SeedAsync(bool resetIndex = false)
        {
            if (resetIndex)
            {
                await ResetIndexAsync();
            }

            var container = GetOrCreateContainer();

            // Remove previous test pages (deleting also removes them from the index)
            foreach (var child in _contentRepository.GetChildren<IContent>(container.ContentLink).ToList())
            {
                _contentRepository.Delete(child.ContentLink, true, AccessLevel.NoAccess);
            }

            var pages = new List<AlloyPage>();
            foreach (var document in TestDataset.Documents)
            {
                var page = _contentRepository.GetDefault<AlloyPage>(container.ContentLink, Culture);
                page.Name = document.Title;
                page.MetaTitle = document.Key;
                page.MainBody = new XhtmlString("<p>" + System.Net.WebUtility.HtmlEncode(document.Title) + "</p>");
                var reference = _contentRepository.Save(page, SaveAction.Publish, AccessLevel.NoAccess);
                pages.Add(_contentRepository.Get<AlloyPage>(reference, Culture));
            }

            // Publishing indexes through the event queue; index explicitly so the tests do not race the queue.
#if NETFRAMEWORK
            ContentIndexer.Instance.Index(pages);
#else
            await ContentIndexer.Instance.IndexAsync(pages);
#endif

            await SeedSynonymsAsync();

            return new { container = container.ContentLink.ID, pages = pages.Count, synonyms = TestDataset.Synonyms.Length, site = SiteName };
        }

        /// <summary>
        /// Deletes ALL documents and ALL synonyms in the configured index. Only for dedicated test indexes.
        /// </summary>
        private async Task ResetIndexAsync()
        {
#if NETFRAMEWORK
            _client.DeleteByQuery(new MatchAllQuery(), command => { });
#else
            await _client.DeleteByQueryAsync(new MatchAllQuery(), command => { });
#endif

            while (true)
            {
                var batch = await ListSynonymsAsync(100, null);
                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var synonym in batch)
                {
                    await DeleteSynonymAsync(synonym.Id);
                }
            }
        }

        /// <summary>Number of seeded test pages that are searchable (no query string involved).</summary>
        public async Task<int> CountAsync()
        {
            var containerId = GetOrCreateContainer().ContentLink.ID.ToString(CultureInfo.InvariantCulture);
            var search = _client.Search<AlloyPage>(EPiServer.Find.Language.English)
                                .Filter(x => x.Ancestors().Match(containerId))
                                .Select(x => x.Name)
                                .Take(0);
            return (await GetResultAsync(search)).TotalMatching;
        }

        private async Task SeedSynonymsAsync()
        {
            var tags = new[] { "language:" + TestDataset.Language };

            var existing = await ListSynonymsAsync(1000, tags);
            var testPhrases = new HashSet<string>(TestDataset.Synonyms.Select(s => s.Phrase), StringComparer.OrdinalIgnoreCase);
            foreach (var synonym in existing.Where(s => testPhrases.Contains(s.Phrase)))
            {
                await DeleteSynonymAsync(synonym.Id);
            }

            foreach (var synonym in TestDataset.Synonyms)
            {
                await AddSynonymAsync(new Synonym(synonym.Phrase, synonym.Synonym, synonym.Bidirectional));
            }
        }

        // ---- Find calls: async on CMS 12 / Find 17, synchronous on CMS 11 / Find 13 (no async API there)

#if NETFRAMEWORK
        private Task<List<Synonym>> ListSynonymsAsync(int size, IEnumerable<string> tags)
            => Task.FromResult(_client.Optimizations().Synonyms().List(size, 0, tags).Hits.ToList());

        private Task DeleteSynonymAsync(string id)
        {
            _client.Optimizations().Synonyms().Delete(id);
            return Task.CompletedTask;
        }

        private Task AddSynonymAsync(Synonym synonym)
        {
            _client.Optimizations().Synonyms().Add(synonym, EPiServer.Find.Language.English);
            return Task.CompletedTask;
        }

        private static Task<SearchResults<T>> GetResultAsync<T>(ISearch<T> search) => Task.FromResult(search.GetResult());

        private static Task<UnifiedSearchResults> GetUnifiedResultAsync(ITypeSearch<ISearchContent> search) => Task.FromResult(search.GetResult());
#else
        private EPiServer.Find.Optimizations.Synonyms.SynonymsClient Synonyms => (EPiServer.Find.Optimizations.Synonyms.SynonymsClient)_client.Optimizations().Synonyms();

        private async Task<List<Synonym>> ListSynonymsAsync(int size, IEnumerable<string> tags)
            => (await Synonyms.ListAsync(size, 0, tags)).Hits.ToList();

        private Task DeleteSynonymAsync(string id) => Synonyms.DeleteAsync(id);

        private Task AddSynonymAsync(Synonym synonym) => Synonyms.AddAsync(synonym, EPiServer.Find.Language.English);

        private static Task<SearchResults<T>> GetResultAsync<T>(ISearch<T> search) => search.GetResultAsync();

        private static Task<UnifiedSearchResults> GetUnifiedResultAsync(ITypeSearch<ISearchContent> search) => search.GetResultAsync();
#endif

        private AlloyContainer GetOrCreateContainer()
        {
            var start = ContentReference.StartPage;
            var container = _contentRepository.GetChildren<AlloyContainer>(start, Culture)
                                              .FirstOrDefault(c => c.Name == TestDataset.ContainerName);
            if (container != null)
            {
                return container;
            }

            container = _contentRepository.GetDefault<AlloyContainer>(start, Culture);
            container.Name = TestDataset.ContainerName;
            var reference = _contentRepository.Save(container, SaveAction.Publish, AccessLevel.NoAccess);
            return _contentRepository.Get<AlloyContainer>(reference, Culture);
        }

        public async Task<ToolboxSearchResponse> SearchAsync(ToolboxSearchRequest request)
        {
            var container = GetOrCreateContainer();
            var containerId = container.ContentLink.ID.ToString(CultureInfo.InvariantCulture);
            var keyByTitle = TestDataset.Documents.ToDictionary(d => d.Title, d => d.Key);

            List<string> titles;
            string queryJson;

            if (request.Unified)
            {
                // The way the README uses the toolbox: UnifiedSearch + SearchTitle
                var search = ApplyToolbox(_client.UnifiedSearch(EPiServer.Find.Language.English).For(request.Q ?? string.Empty), request, x => x.SearchTitle, x => x.SearchText)
                    .Filter(x => ((IContent)x).Ancestors().Match(containerId))
                    .Take(100);
                titles = (await GetUnifiedResultAsync(search)).Select(hit => System.Net.WebUtility.HtmlDecode(hit.Title)).ToList();
                queryJson = GetQueryJson(search);
            }
            else
            {
                var search = ApplyToolbox(_client.Search<AlloyPage>(EPiServer.Find.Language.English).For(request.Q ?? string.Empty), request, x => x.Name, x => x.SearchText())
                    .Filter(x => x.Ancestors().Match(containerId))
                    .Take(100);
                titles = (await GetResultAsync(search.Select(x => x.Name))).ToList();
                queryJson = GetQueryJson(search);
            }

            return new ToolboxSearchResponse
            {
                Site = SiteName,
                Hits = titles.Select(title => keyByTitle.TryGetValue(title, out var key) ? key : title).ToList(),
                Query = queryJson
            };
        }

        /// <summary>Applies the toolbox extensions in the order the README documents.</summary>
        private static IQueriedSearch<T, QueryStringQuery> ApplyToolbox<T>(IQueriedSearch<T, QueryStringQuery> search, ToolboxSearchRequest request, Expression<Func<T, string>> titleField, Expression<Func<T, string>> bodyField)
        {
            if (request.InFields)
            {
                search = search.InField(titleField).InField(bodyField);
            }

            if (string.Equals(request.Mode, "and", StringComparison.OrdinalIgnoreCase))
            {
                search = search.WithAndAsDefaultOperator();
            }

            var useMsm = string.Equals(request.Mode, "msm", StringComparison.OrdinalIgnoreCase);
            if (useMsm && !request.MsmAfterSynonyms)
            {
                search = search.MinimumShouldMatch(request.Msm);
            }

            if (request.Synonyms)
            {
                // Zero cache duration: always read the synonyms seeded by the test run
                search = search.UsingSynonymsImproved(TimeSpan.Zero);
            }

            if (useMsm && request.MsmAfterSynonyms)
            {
                search = search.MinimumShouldMatch(request.Msm);
            }

            if (request.Relevance)
            {
                search = search.UsingRelevanceImproved(titleField);
            }

            if (request.Fuzzy)
            {
                search = search.FuzzyMatch(titleField);
            }

            if (request.Wildcard)
            {
                search = search.WildcardMatch(titleField);
            }

            return search;
        }

        private static string GetQueryJson(ISearch search)
        {
            var context = new SearchContext();
            search.ApplyActions(context);
            var json = Serializer.Serialize(Serializer.CreateDefault(), context.RequestBody);
            return JObject.Parse(json)["query"]?.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
