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
        private readonly FindToolboxOptions _options;
        private static readonly System.Threading.SemaphoreSlim OptionsLock = new System.Threading.SemaphoreSlim(1, 1);

        public ToolboxTestService(IContentRepository contentRepository, IClient client, FindToolboxOptions options)
        {
            _contentRepository = contentRepository;
            _client = client;
            _options = options;
        }

        /// <summary>
        /// Applies per-request boost overrides to the (singleton) options and returns an action restoring them.
        /// Test sites only: requests are serialized by the caller.
        /// </summary>
        private Action ApplyBoostOverrides(ToolboxSearchRequest request)
        {
            var saved = new[] { _options.SynonymBoost, _options.SynonymVariationBoostFactor, _options.PhraseBoost, _options.PhrasePrefixBoost, _options.PrefixBoost, _options.FuzzyBoost, _options.WildcardBoost };
            _options.SynonymBoost = request.SynonymBoost ?? _options.SynonymBoost;
            _options.SynonymVariationBoostFactor = request.SynonymVariationBoostFactor ?? _options.SynonymVariationBoostFactor;
            _options.PhraseBoost = request.PhraseBoost ?? _options.PhraseBoost;
            _options.PhrasePrefixBoost = request.PhrasePrefixBoost ?? _options.PhrasePrefixBoost;
            _options.PrefixBoost = request.PrefixBoost ?? _options.PrefixBoost;
            _options.FuzzyBoost = request.FuzzyBoost ?? _options.FuzzyBoost;
            _options.WildcardBoost = request.WildcardBoost ?? _options.WildcardBoost;
            return () =>
            {
                _options.SynonymBoost = saved[0];
                _options.SynonymVariationBoostFactor = saved[1];
                _options.PhraseBoost = saved[2];
                _options.PhrasePrefixBoost = saved[3];
                _options.PrefixBoost = saved[4];
                _options.FuzzyBoost = saved[5];
                _options.WildcardBoost = saved[6];
            };
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

            var container = GetOrCreateContainer(TestDataset.ContainerName);
            var relevanceContainer = GetOrCreateContainer(TestDataset.RelevanceContainerName);

            var pages = SeedPages(container, TestDataset.Documents);
            pages.AddRange(SeedPages(relevanceContainer, TestDataset.RelevanceDocuments));

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
        /// Idempotent: existing test pages are updated in place (stable content IDs), missing ones are created and
        /// pages that are no longer in the data set are deleted (also from the index). Recreating pages on every
        /// run raced with Find's own event indexing queue, which re-added deleted pages later.
        /// </summary>
        private List<AlloyPage> SeedPages(AlloyContainer container, IEnumerable<TestDataset.TestDocument> documents)
        {
            var wanted = documents.ToList();
            var existing = _contentRepository.GetChildren<AlloyPage>(container.ContentLink, Culture).ToList();

            foreach (var obsolete in _contentRepository.GetChildren<IContent>(container.ContentLink)
                                                        .Where(c => !(c is AlloyPage page && wanted.Any(d => d.Title == page.Name)))
                                                        .ToList())
            {
                ContentIndexer.Instance.Delete(obsolete);
                _contentRepository.Delete(obsolete.ContentLink, true, AccessLevel.NoAccess);
            }

            var pages = new List<AlloyPage>();
            foreach (var document in wanted)
            {
                var current = existing.FirstOrDefault(p => p.Name == document.Title);
                var page = current != null
                    ? (AlloyPage)current.CreateWritableClone()
                    : _contentRepository.GetDefault<AlloyPage>(container.ContentLink, Culture);

                page.Name = document.Title;
                page.MetaTitle = document.Key;
                page.MainBody = new XhtmlString("<p>" + System.Net.WebUtility.HtmlEncode(document.Title) + "</p>");
                var reference = _contentRepository.Save(page, SaveAction.Publish | SaveAction.ForceCurrentVersion, AccessLevel.NoAccess);
                pages.Add(_contentRepository.Get<AlloyPage>(reference.ToReferenceWithoutVersion(), Culture));
            }

            return pages;
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

            // Delete by query runs asynchronously in Find: wait until the index is empty, otherwise it
            // also removes pages that are seeded right after.
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (await CountAllAsync() > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000);
            }

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

        private async Task<int> CountAllAsync()
        {
            var search = _client.Search<IContentData>()
                                .Filter(x => !((IContent)x).Name.Match(CacheBuster()))
                                .Select(x => ((IContent)x).Name)
                                .Take(0);
            return (await GetResultAsync(search)).TotalMatching;
        }

        /// <summary>Number of seeded test pages that are searchable (no query string involved).</summary>
        public async Task<object> CountAsync()
        {
            var containers = new[] { TestDataset.ContainerName, TestDataset.RelevanceContainerName }.Select(GetOrCreateContainer).ToList();
            var containerIds = containers.Select(c => c.ContentLink.ID.ToString(CultureInfo.InvariantCulture)).ToList();
            var currentIds = containers.SelectMany(c => _contentRepository.GetChildren<IContent>(c.ContentLink))
                                       .Select(c => c.ContentLink.ID)
                                       .ToList();

            var all = _client.Search<AlloyPage>(EPiServer.Find.Language.English)
                             .Filter(x => x.Ancestors().In(containerIds))
                             .Filter(x => !x.Name.Match(CacheBuster()))
                             .Select(x => x.ContentLink.ID)
                             .Take(1000);
            var indexedIds = (await GetResultAsync(all)).ToList();

            // current: seeded pages that are searchable; stale: documents of earlier seeds still in the index
            return new
            {
                current = indexedIds.Distinct().Count(currentIds.Contains),
                stale = indexedIds.Count(id => !currentIds.Contains(id)),
                expected = currentIds.Count
            };
        }

        private async Task SeedSynonymsAsync()
        {
            var tags = new[] { "language:" + TestDataset.Language };

            var existing = await ListSynonymsAsync(1000, tags);
            var testPhrases = new HashSet<string>(TestDataset.Synonyms.Select(s => s.Phrase), StringComparer.OrdinalIgnoreCase);
            var current = existing.Where(s => testPhrases.Contains(s.Phrase)).ToList();

            // Idempotent: both test sites share the index, so do not touch the synonyms when they are already right
            // (deleting and re-adding them would briefly remove them while the other site's tests run).
            bool Same(Synonym s, TestDataset.TestSynonym t) => s.Phrase == t.Phrase && s.SynonymPhrase == t.Synonym && s.Bidirectional == t.Bidirectional;
            if (current.Count == TestDataset.Synonyms.Length && TestDataset.Synonyms.All(t => current.Any(s => Same(s, t))))
            {
                return;
            }

            foreach (var synonym in current)
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

        private AlloyContainer GetOrCreateContainer(string name)
        {
            var start = ContentReference.StartPage;
            var container = _contentRepository.GetChildren<AlloyContainer>(start, Culture)
                                              .FirstOrDefault(c => c.Name == name);
            if (container != null)
            {
                return container;
            }

            container = _contentRepository.GetDefault<AlloyContainer>(start, Culture);
            container.Name = name;
            var reference = _contentRepository.Save(container, SaveAction.Publish, AccessLevel.NoAccess);
            return _contentRepository.Get<AlloyContainer>(reference, Culture);
        }

        public async Task<ToolboxSearchResponse> SearchAsync(ToolboxSearchRequest request)
        {
            // Overrides mutate shared options: serialize (test sites only)
            await OptionsLock.WaitAsync();
            var restore = ApplyBoostOverrides(request);
            try
            {
                return await SearchCoreAsync(request);
            }
            finally
            {
                restore();
                OptionsLock.Release();
            }
        }

        private async Task<ToolboxSearchResponse> SearchCoreAsync(ToolboxSearchRequest request)
        {
            var relevanceSet = string.Equals(request.Set, "relevance", StringComparison.OrdinalIgnoreCase);
            var container = GetOrCreateContainer(relevanceSet ? TestDataset.RelevanceContainerName : TestDataset.ContainerName);
            var containerId = container.ContentLink.ID.ToString(CultureInfo.InvariantCulture);
            var keyByTitle = TestDataset.AllDocuments.ToDictionary(d => d.Title, d => d.Key);

            List<string> titles;
            List<double> scores;
            string queryJson;

            if (request.Unified)
            {
                // The way the README uses the toolbox: UnifiedSearch + SearchTitle
                var search = ApplyToolbox(_client.UnifiedSearch(EPiServer.Find.Language.English).For(request.Q ?? string.Empty), request, x => x.SearchTitle, x => x.SearchText)
                    .Filter(x => ((IContent)x).Ancestors().Match(containerId))
                    // Both test sites may share an index: only this site's page type (content IDs can coincide)
                    .Filter(x => x.MatchTypeHierarchy(typeof(AlloyPage)))
                    .Filter(x => !x.SearchTitle.Match(CacheBuster()))
                    .Take(100);
                var result = await GetUnifiedResultAsync(search);
                titles = result.Hits.Select(hit => System.Net.WebUtility.HtmlDecode(hit.Document.Title)).ToList();
                scores = result.Hits.Select(hit => (double)(hit.Score ?? 0)).ToList();
                queryJson = GetQueryJson(search);
            }
            else
            {
                var search = ApplyToolbox(_client.Search<AlloyPage>(EPiServer.Find.Language.English).For(request.Q ?? string.Empty), request, x => x.Name, x => x.SearchText())
                    .Filter(x => x.Ancestors().Match(containerId))
                    .Filter(x => !x.Name.Match(CacheBuster()))
                    .Take(100);
                var result = await GetResultAsync(search.Select(x => x.Name));
                titles = result.Hits.Select(hit => hit.Document).ToList();
                scores = result.Hits.Select(hit => (double)(hit.Score ?? 0)).ToList();
                queryJson = GetQueryJson(search);
            }

            return new ToolboxSearchResponse
            {
                Site = SiteName,
                Hits = titles.Select(title => keyByTitle.TryGetValue(title, out var key) ? key : title).ToList(),
                Scores = scores,
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

        /// <summary>Indexes all site content (e.g. the Alloy demo pages) so the site's search page has something to find.</summary>
        public async Task<object> IndexSiteAsync()
        {
            var content = _contentRepository.GetDescendents(ContentReference.RootPage)
                .Select(reference => _contentRepository.TryGet(reference, out IContent item) ? item : null)
                .Where(item => item != null)
                .ToList();

            foreach (var batch in content.Select((item, i) => new { item, i }).GroupBy(x => x.i / 100, x => x.item))
            {
#if NETFRAMEWORK
                ContentIndexer.Instance.Index(batch);
#else
                await ContentIndexer.Instance.IndexAsync(batch);
#endif
            }

            return new { indexed = content.Count, site = SiteName };
        }

        /// <summary>
        /// Runs a CMS UI search provider (e.g. "Find Toolbox - Pages") the way the edit UI does and returns the
        /// test document keys of the results, in order.
        /// </summary>
        public List<string> SearchWithProvider(EPiServer.Shell.Search.ISearchProvider provider, string q)
        {
            var keyByTitle = TestDataset.AllDocuments.ToDictionary(d => d.Title, d => d.Key);
            // Search roots = the test containers, like the edit UI passes roots (both test sites may share an index)
            var roots = new[] { TestDataset.ContainerName, TestDataset.RelevanceContainerName }
                .Select(name => GetOrCreateContainer(name).ContentLink.ID.ToString(CultureInfo.InvariantCulture))
                .ToList();
            var results = provider.Search(new EPiServer.Shell.Search.Query(q) { MaxResults = 20, SearchRoots = roots });
            return results.Select(r => System.Net.WebUtility.HtmlDecode(r.Title))
                          .Select(title => keyByTitle.TryGetValue(title, out var key) ? key : title)
                          .ToList();
        }

        /// <summary>
        /// The Find service sits behind a CDN that caches identical requests. A random, always-true filter makes
        /// every request unique so tests see the index as it is right now (e.g. right after reseeding).
        /// </summary>
        private static string CacheBuster() => "nocache-" + Guid.NewGuid().ToString("N");

        private static string GetQueryJson(ISearch search)
        {
            var context = new SearchContext();
            search.ApplyActions(context);
            var json = Serializer.Serialize(Serializer.CreateDefault(), context.RequestBody);
            return JObject.Parse(json)["query"]?.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
