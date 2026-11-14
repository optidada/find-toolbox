using EPiServer;
using EPiServer.Core;
using EPiServer.DataAccess;
using EPiServer.Find;
using EPiServer.Find.Api.Facets;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Filters;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Cms;
using EPiServer.Find.Json;
using EPiServer.Labs.Find.Toolbox;
using EPiServer.Security;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace ToolboxTestHarness.ProductDemo
{
    /// <summary>
    /// Faceted product search with the Find Toolbox. Facet counts are computed per facet with the filters of all OTHER
    /// facets applied (multi-select facets): selecting a brand keeps the counts of the other brands, while the category,
    /// color and price counts narrow down.
    /// </summary>
    public class ProductSearchService
    {
        private readonly IContentRepository _contentRepository;
        private readonly IClient _client;

        public ProductSearchService(IContentRepository contentRepository, IClient client)
        {
            _contentRepository = contentRepository;
            _client = client;
        }

        private static CultureInfo Culture => CultureInfo.GetCultureInfo(TestDataset.Language);

        /// <summary>Idempotent: products are updated in place (stable IDs); returns the pages to index.</summary>
        public List<DemoProductPage> SeedProducts(ContentReference container)
        {
            var existing = _contentRepository.GetChildren<DemoProductPage>(container, Culture).ToList();
            var wanted = ProductDataset.Products;

            foreach (var obsolete in _contentRepository.GetChildren<IContent>(container)
                                                        .Where(c => !(c is DemoProductPage p && wanted.Any(w => w.Name == p.Name)))
                                                        .ToList())
            {
                ContentIndexer.Instance.Delete(obsolete);
                _contentRepository.Delete(obsolete.ContentLink, true, AccessLevel.NoAccess);
            }

            var pages = new List<DemoProductPage>();
            foreach (var product in wanted)
            {
                var current = existing.FirstOrDefault(p => p.Name == product.Name);
                var page = current != null
                    ? (DemoProductPage)current.CreateWritableClone()
                    : _contentRepository.GetDefault<DemoProductPage>(container, Culture);

                page.Name = product.Name;
                page.ProductCategory = product.Category;
                page.Brand = product.Brand;
                page.Color = product.Color;
                page.Price = product.Price;
                page.Description = product.Description;

                var reference = _contentRepository.Save(page, SaveAction.Publish | SaveAction.ForceCurrentVersion, AccessLevel.NoAccess);
                pages.Add(_contentRepository.Get<DemoProductPage>(reference.ToReferenceWithoutVersion(), Culture));
            }

            return pages;
        }

        /// <summary>
        /// One Find request per search (plus a cached facet value list when facets are selected):
        /// - nothing selected: hits + terms/range facets in one request, counts are exact;
        /// - facets selected: hits are narrowed with FilterHits (post filter, does not affect facets) and every facet
        ///   value is counted with a FilterFacet of "the other facets' selections AND this value", all in one request.
        ///   Which values a facet has comes from the terms facets of the query alone, cached for a minute.
        /// </summary>
        public async Task<ProductSearchResult> SearchAsync(ProductSearchRequest request, ContentReference container)
        {
            var containerId = container.ID.ToString(CultureInfo.InvariantCulture);
            var chain = new List<string>();
            var baseSearch = BaseSearch(request, containerId, chain);
            var anySelected = request.Category.Count + request.Brand.Count + request.Color.Count + request.Price.Count > 0;
            var requests = 0;

            var result = new ProductSearchResult { Request = request, ToolboxChain = string.Join(Environment.NewLine, chain) };

            if (!anySelected)
            {
                var search = WithTermsFacets(baseSearch)
                    .Take(40)
                    .Select(x => new ProductHit { Name = x.Name, Category = x.ProductCategory, Brand = x.Brand, Color = x.Color, Price = x.Price });
                var response = await GetResultAsync(Cache(search, request, TimeSpan.FromSeconds(30)));
                requests++;

                result.Total = response.TotalMatching;
                result.Hits = Hits(response);
                result.Facets = FacetsFromTerms(response);
                result.QueryJson = QueryJson(search);
            }
            else
            {
                // Facet values for the query, independent of the selections: cacheable
                var valuesSearch = WithTermsFacets(baseSearch).Take(0).Select(x => x.Name);
                var values = FacetsFromTerms(await GetResultAsync(Cache(valuesSearch, request, TimeSpan.FromMinutes(1))));
                requests++;

                // Selected values must always be listed, also when they have no hits for this query
                AddSelected(values, "category", request.Category);
                AddSelected(values, "brand", request.Brand);
                AddSelected(values, "color", request.Color);

                var search = baseSearch.FilterHits(SelectedFilter(request, except: null));
                var facetNames = new Dictionary<string, (string Facet, string Value)>();
                foreach (var facet in values)
                {
                    foreach (var value in facet.Value.Keys)
                    {
                        var name = "f" + facetNames.Count;
                        facetNames[name] = (facet.Key, value);
                        search = search.FilterFacet(name, new AndFilter(new[] { SelectedFilter(request, except: facet.Key), ValueFilter(facet.Key, value) }.Where(f => f != null).ToArray()));
                    }
                }

                var hitsSearch = search
                    .Take(40)
                    .Select(x => new ProductHit { Name = x.Name, Category = x.ProductCategory, Brand = x.Brand, Color = x.Color, Price = x.Price });
                var response = await GetResultAsync(Cache(hitsSearch, request, TimeSpan.FromSeconds(30)));
                requests++;

                result.Total = response.TotalMatching;
                result.Hits = Hits(response);
                result.QueryJson = QueryJson(hitsSearch);
                foreach (var facet in values)
                {
                    result.Facets[facet.Key] = facet.Value.Keys.ToDictionary(v => v, v => 0);
                }
                foreach (var named in facetNames)
                {
                    result.Facets[named.Value.Facet][named.Value.Value] = response.FilterFacet(named.Key)?.Count ?? 0;
                }
            }

            result.ToolboxChain += Environment.NewLine + "// Find requests for this page: " + requests + (request.NoCache ? "" : " (or fewer: cached)");
            return result;
        }

        private ITypeSearch<DemoProductPage> WithTermsFacets(ITypeSearch<DemoProductPage> search)
            => search
                .TermsFacetFor(x => x.ProductCategory, f => f.Size = 20)
                .TermsFacetFor(x => x.Brand, f => f.Size = 20)
                .TermsFacetFor(x => x.Color, f => f.Size = 20)
                .RangeFacetFor(x => x.Price, ProductDataset.PriceRanges.Select(r => new NumericRange(r.From, r.To)).ToArray());

        /// <summary>Identical searches are served from the cache unless the caller needs fresh results (tests).</summary>
        private static ISearch<T> Cache<T>(ISearch<T> search, ProductSearchRequest request, TimeSpan duration)
            => request.NoCache ? search : search.StaticallyCacheFor(duration);

        private static List<ProductHit> Hits(SearchResults<ProductHit> response)
            => response.Hits.Select(h => { h.Document.Score = h.Score ?? 0; return h.Document; }).ToList();

        private static void AddSelected(Dictionary<string, Dictionary<string, int>> facets, string facet, IEnumerable<string> selected)
        {
            foreach (var value in selected.Where(v => !facets[facet].ContainsKey(v)))
            {
                facets[facet][value] = 0;
            }
        }

        private static Dictionary<string, Dictionary<string, int>> FacetsFromTerms<T>(SearchResults<T> response)
        {
            var terms = response.Facets.OfType<TermsFacet>().ToList();
            Dictionary<string, int> Terms(string field) => terms
                .Where(t => t.Name.StartsWith(field, StringComparison.OrdinalIgnoreCase))
                .SelectMany(t => t.Terms)
                .ToDictionary(t => t.Term, t => t.Count);

            return new Dictionary<string, Dictionary<string, int>>
            {
                ["category"] = Terms("ProductCategory"),
                ["brand"] = Terms("Brand"),
                ["color"] = Terms("Color"),
                ["price"] = Ranges(response),
            };
        }

        /// <summary>The selected facet values of all facets except <paramref name="except"/> (null = all), or null if none.</summary>
        private Filter SelectedFilter(ProductSearchRequest request, string except)
        {
            var filters = new List<Filter>();
            foreach (var facet in new[] { "category", "brand", "color", "price" }.Where(f => f != except))
            {
                var selected = facet == "category" ? request.Category : facet == "brand" ? request.Brand : facet == "color" ? request.Color : request.Price;
                if (selected.Count > 0)
                {
                    filters.Add(new OrFilter(selected.Select(v => ValueFilter(facet, v)).ToArray()));
                }
            }

            return filters.Count == 0 ? null : filters.Count == 1 ? filters[0] : new AndFilter(filters.ToArray());
        }

        private Filter ValueFilter(string facet, string value)
        {
            var builder = _client.BuildFilter<DemoProductPage>();
            switch (facet)
            {
                case "category": return builder.And(x => x.ProductCategory.Match(value));
                case "brand": return builder.And(x => x.Brand.Match(value));
                case "color": return builder.And(x => x.Color.Match(value));
                default:
                    var range = ProductDataset.PriceRanges.First(r => r.Key == value);
                    if (range.From == null) return builder.And(x => x.Price.LessThan(range.To.Value));
                    if (range.To == null) return builder.And(x => x.Price.GreaterThan(range.From.Value - 0.001));
                    return builder.And(x => x.Price.InRange(range.From.Value, range.To.Value - 0.001));
            }
        }

#if NETFRAMEWORK
        private static Task<SearchResults<T>> GetResultAsync<T>(ISearch<T> search) => Task.FromResult(search.GetResult());
#else
        private static Task<SearchResults<T>> GetResultAsync<T>(ISearch<T> search) => search.GetResultAsync();
#endif

        /// <summary>The query (with the toolbox) and the container filter, without facet filters.</summary>
        private ITypeSearch<DemoProductPage> BaseSearch(ProductSearchRequest request, string containerId, List<string> chain)
        {
            var language = request.Lang == "sv" ? EPiServer.Find.Language.Swedish
                         : request.Lang == "none" ? EPiServer.Find.Language.None
                         : EPiServer.Find.Language.English;
            var languageName = request.Lang == "sv" ? "Language.Swedish" : request.Lang == "none" ? "Language.None" : "Language.English";

            ITypeSearch<DemoProductPage> search = _client.Search<DemoProductPage>(language);
            chain.Add("client.Search<DemoProductPage>(" + languageName + ")");

            if (!string.IsNullOrWhiteSpace(request.Q))
            {
                IQueriedSearch<DemoProductPage, QueryStringQuery> queried = search.For(request.Q)
                    .InField(x => x.Name, 5)
                    .InField(x => x.ProductCategory, 2)
                    .InField(x => x.Brand, 2)
                    .InField(x => x.Description);
                chain.Add($"    .For(\"{request.Q}\")");
                chain.Add("    .InField(x => x.Name, 5).InField(x => x.ProductCategory, 2).InField(x => x.Brand, 2).InField(x => x.Description)");

                if (request.Engine == "find" || request.Engine == "find-and")
                {
                    // Plain Find for comparison: same fields, built-in synonyms, default operator OR or AND
                    if (request.Engine == "find-and")
                    {
                        queried = queried.WithAndAsDefaultOperator();
                        chain.Add("    .WithAndAsDefaultOperator()");
                    }

                    queried = queried.UsingSynonyms();
                    chain.Add("    .UsingSynonyms()   // Find's built-in synonyms");
                    search = queried;
                    chain.Add("    .Filter(selected facets) .TermsFacetFor(...) .RangeFacetFor(x => x.Price, ...)");
                    return Scope(search, request, containerId);
                }

                if (!string.IsNullOrWhiteSpace(request.Msm))
                {
                    queried = queried.MinimumShouldMatch(request.Msm);
                    chain.Add($"    .MinimumShouldMatch(\"{request.Msm}\")");
                }

                if (request.Synonyms)
                {
                    // Short cache so synonyms just added in the Find UI show up quickly in the demo
                    queried = queried.UsingSynonymsImproved(TimeSpan.FromSeconds(10));
                    chain.Add("    .UsingSynonymsImproved()");
                }

                if (request.Relevance)
                {
                    queried = queried.UsingRelevanceImproved(x => x.Name);
                    chain.Add("    .UsingRelevanceImproved(x => x.Name)");
                }

                if (request.Fuzzy)
                {
                    queried = queried.FuzzyMatch(x => x.Name);
                    chain.Add("    .FuzzyMatch(x => x.Name)");
                }

                if (request.Wildcard)
                {
                    queried = queried.WildcardMatch(x => x.Name);
                    chain.Add("    .WildcardMatch(x => x.Name)");
                }

                search = queried;
            }

            chain.Add("    .Filter(selected facets) .TermsFacetFor(...) .RangeFacetFor(x => x.Price, ...)");

            return Scope(search, request, containerId);
        }

        private static Dictionary<string, int> Ranges(IHasFacetResults result)
        {
            var facet = result.Facets.OfType<NumericRangeFacet>().FirstOrDefault();
            var counts = new Dictionary<string, int>();
            foreach (var range in ProductDataset.PriceRanges)
            {
                var match = facet?.Ranges.FirstOrDefault(r => r.From == range.From && r.To == range.To);
                counts[range.Key] = match?.Count ?? 0;
            }

            return counts;
        }

        private static ITypeSearch<DemoProductPage> Scope(ITypeSearch<DemoProductPage> search, ProductSearchRequest request, string containerId)
        {
            search = search.Filter(x => x.Ancestors().Match(containerId));

            // Tests need the index as it is right now: the Find service sits behind a CDN, so make the request unique
            return request.NoCache ? search.Filter(x => !x.Name.Match("nocache-" + Guid.NewGuid().ToString("N"))) : search;
        }

        private static string QueryJson(ISearch search)
        {
            var context = new SearchContext();
            search.ApplyActions(context);
            var json = Serializer.Serialize(Serializer.CreateDefault(), context.RequestBody);
            return JObject.Parse(json)["query"]?.ToString(Newtonsoft.Json.Formatting.Indented);
        }
    }
}
