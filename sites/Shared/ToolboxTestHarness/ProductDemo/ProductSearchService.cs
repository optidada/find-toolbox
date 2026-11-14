using EPiServer;
using EPiServer.Core;
using EPiServer.DataAccess;
using EPiServer.Find;
using EPiServer.Find.Api.Facets;
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

        public async Task<ProductSearchResult> SearchAsync(ProductSearchRequest request, ContentReference container)
        {
            var containerId = container.ID.ToString(CultureInfo.InvariantCulture);
            var chain = new List<string>();
            var baseSearch = BaseSearch(request, containerId, chain);

            // Hits: all filters
            var hitsSearch = ApplyFilters(baseSearch, request, except: null)
                .Take(40)
                .Select(x => new ProductHit { Name = x.Name, Category = x.ProductCategory, Brand = x.Brand, Color = x.Color, Price = x.Price });

            // Facets: one search per facet, with the filters of the other facets
            var categorySearch = ApplyFilters(baseSearch, request, "category").TermsFacetFor(x => x.ProductCategory, f => f.Size = 20).Take(0).Select(x => x.Name);
            var brandSearch = ApplyFilters(baseSearch, request, "brand").TermsFacetFor(x => x.Brand, f => f.Size = 20).Take(0).Select(x => x.Name);
            var colorSearch = ApplyFilters(baseSearch, request, "color").TermsFacetFor(x => x.Color, f => f.Size = 20).Take(0).Select(x => x.Name);
            var priceSearch = ApplyFilters(baseSearch, request, "price")
                .RangeFacetFor(x => x.Price, ProductDataset.PriceRanges.Select(r => new NumericRange(r.From, r.To)).ToArray())
                .Take(0).Select(x => x.Name);

#if NETFRAMEWORK
            var hits = hitsSearch.GetResult();
            var categories = categorySearch.GetResult();
            var brands = brandSearch.GetResult();
            var colors = colorSearch.GetResult();
            var prices = priceSearch.GetResult();
            await Task.CompletedTask;
#else
            var hitsTask = hitsSearch.GetResultAsync();
            var categoriesTask = categorySearch.GetResultAsync();
            var brandsTask = brandSearch.GetResultAsync();
            var colorsTask = colorSearch.GetResultAsync();
            var pricesTask = priceSearch.GetResultAsync();
            await Task.WhenAll(hitsTask, categoriesTask, brandsTask, colorsTask, pricesTask);
            var hits = hitsTask.Result;
            var categories = categoriesTask.Result;
            var brands = brandsTask.Result;
            var colors = colorsTask.Result;
            var prices = pricesTask.Result;
#endif

            var result = new ProductSearchResult
            {
                Request = request,
                Total = hits.TotalMatching,
                Hits = hits.Hits.Select(h => { h.Document.Score = h.Score ?? 0; return h.Document; }).ToList(),
                ToolboxChain = string.Join(Environment.NewLine, chain),
                QueryJson = QueryJson(hitsSearch),
            };

            result.Facets["category"] = Terms(categories);
            result.Facets["brand"] = Terms(brands);
            result.Facets["color"] = Terms(colors);
            result.Facets["price"] = Ranges(prices);
            return result;
        }

        /// <summary>The query (with the toolbox) and the container filter, without facet filters.</summary>
        private ITypeSearch<DemoProductPage> BaseSearch(ProductSearchRequest request, string containerId, List<string> chain)
        {
            ITypeSearch<DemoProductPage> search = _client.Search<DemoProductPage>(EPiServer.Find.Language.English);
            chain.Add("client.Search<DemoProductPage>(Language.English)");

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
                    return search
                        .Filter(x => x.Ancestors().Match(containerId))
                        .Filter(x => !x.Name.Match("nocache-" + Guid.NewGuid().ToString("N")));
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

            return search
                .Filter(x => x.Ancestors().Match(containerId))
                // The Find service sits behind a CDN: make every request unique
                .Filter(x => !x.Name.Match("nocache-" + Guid.NewGuid().ToString("N")));
        }

        private ITypeSearch<DemoProductPage> ApplyFilters(ITypeSearch<DemoProductPage> search, ProductSearchRequest request, string except)
        {
            if (except != "category" && request.Category.Count > 0)
            {
                search = search.Filter(x => x.ProductCategory.In(request.Category));
            }

            if (except != "brand" && request.Brand.Count > 0)
            {
                search = search.Filter(x => x.Brand.In(request.Brand));
            }

            if (except != "color" && request.Color.Count > 0)
            {
                search = search.Filter(x => x.Color.In(request.Color));
            }

            if (except != "price" && request.Price.Count > 0)
            {
                var filter = _client.BuildFilter<DemoProductPage>();
                foreach (var range in ProductDataset.PriceRanges.Where(r => request.Price.Contains(r.Key)))
                {
                    if (range.From == null)
                    {
                        filter = filter.Or(x => x.Price.LessThan(range.To.Value));
                    }
                    else if (range.To == null)
                    {
                        filter = filter.Or(x => x.Price.GreaterThan(range.From.Value - 0.001));
                    }
                    else
                    {
                        filter = filter.Or(x => x.Price.InRange(range.From.Value, range.To.Value - 0.001));
                    }
                }

                search = search.Filter(filter);
            }

            return search;
        }

        private static Dictionary<string, int> Terms(IHasFacetResults<string> result)
        {
            var facet = result.Facets.OfType<TermsFacet>().FirstOrDefault();
            return facet == null
                ? new Dictionary<string, int>()
                : facet.Terms.ToDictionary(t => t.Term, t => t.Count);
        }

        private static Dictionary<string, int> Ranges(IHasFacetResults<string> result)
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

        private static string QueryJson(ISearch search)
        {
            var context = new SearchContext();
            search.ApplyActions(context);
            var json = Serializer.Serialize(Serializer.CreateDefault(), context.RequestBody);
            return JObject.Parse(json)["query"]?.ToString(Newtonsoft.Json.Formatting.Indented);
        }
    }
}
