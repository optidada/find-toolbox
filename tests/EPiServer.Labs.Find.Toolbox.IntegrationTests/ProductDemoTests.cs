using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ToolboxTestHarness.ProductDemo;
using Xunit;
using Xunit.Abstractions;

namespace EPiServer.Labs.Find.Toolbox.IntegrationTests
{
    /// <summary>
    /// The faceted product search demo (/product-demo): hits and facet counts must agree, also with synonyms, typos and
    /// multi-select facets (each facet is counted with the filters of the OTHER facets).
    /// </summary>
    public class ProductDemoTests
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private readonly ITestOutputHelper _output;

        public ProductDemoTests(ITestOutputHelper output) => _output = output;

        public static IEnumerable<object[]> Sites() => SiteClient.Sites.Select(s => new object[] { s });

        private async Task<ProductSearchResult> Search(string site, string query)
        {
            var client = await SiteClient.GetSeededAsync(site);
            Skip.If(client == null, $"Set TOOLBOX_{site.ToUpperInvariant()}_URL to run against the {site} site.");

            var url = SiteClient.GetUrl(site).TrimEnd('/') + "/toolbox-test/products?nocache=true&" + query;
            var result = JsonSerializer.Deserialize<ProductSearchResult>(await Http.GetStringAsync(url), JsonOptions);
            _output.WriteLine($"{site} {query}: {result.Total} hits: {string.Join(", ", result.Hits.Select(h => h.Name))}");
            foreach (var facet in result.Facets)
            {
                _output.WriteLine($"  {facet.Key}: {string.Join(", ", facet.Value.Select(v => v.Key + "=" + v.Value))}");
            }
            return result;
        }

        private static string Q(string q) => "q=" + Uri.EscapeDataString(q);

        [SkippableTheory]
        [MemberData(nameof(Sites))]
        public async Task Facet_counts_add_up_to_the_number_of_products(string site)
        {
            foreach (var query in new[] { "", Q("laptop"), Q("tv"), Q("fabrikam camera"), Q("smartphone") })
            {
                var result = await Search(site, query);

                Assert.True(result.Total > 0, query);
                foreach (var facet in new[] { "category", "brand", "color", "price" })
                {
                    // every product has exactly one value per facet
                    Assert.Equal(result.Total, result.Facets[facet].Values.Sum());
                }
            }
        }

        [SkippableTheory]
        [MemberData(nameof(Sites))]
        public async Task Browsing_without_a_query_shows_all_products(string site)
        {
            var result = await Search(site, "");

            Assert.Equal(ProductDataset.Products.Length + ProductDataset.SwedishProducts.Length, result.Total);
            Assert.Equal(8, result.Facets["category"]["Laptops"]);
            Assert.Equal(ProductDataset.Products.Concat(ProductDataset.SwedishProducts).Count(p => p.Brand == "Fabrikam"), result.Facets["brand"]["Fabrikam"]);
        }

        [SkippableTheory]
        [InlineData("cms11", "laptop")]
        [InlineData("cms11", "notebook")]
        [InlineData("cms12", "laptop")]
        [InlineData("cms12", "notebook")]
        public async Task Synonyms_find_all_laptops_and_the_facets_follow(string site, string word)
        {
            var result = await Search(site, Q(word));

            Assert.Equal(8, result.Total);
            Assert.Equal(new Dictionary<string, int> { ["Laptops"] = 8 }, result.Facets["category"]);
        }

        [SkippableTheory]
        [InlineData("cms11", "tv", 8)]
        [InlineData("cms11", "television", 8)]
        [InlineData("cms11", "mobile phone", 7)]   // the flip phone is neither a mobile phone nor a smartphone by name
        [InlineData("cms11", "smartphone", 7)]
        [InlineData("cms12", "tv", 8)]
        [InlineData("cms12", "television", 8)]
        [InlineData("cms12", "mobile phone", 7)]
        [InlineData("cms12", "smartphone", 7)]
        public async Task Synonyms_in_both_directions(string site, string query, int expected)
        {
            var result = await Search(site, Q(query));

            Assert.Equal(expected, result.Total);
        }

        [SkippableTheory]
        [MemberData(nameof(Sites))]
        public async Task Selecting_a_brand_keeps_the_other_brands_counts_and_narrows_the_other_facets(string site)
        {
            var result = await Search(site, Q("laptop") + "&brand=Fabrikam");

            Assert.Equal(2, result.Total);
            Assert.All(result.Hits, h => Assert.Equal("Fabrikam", h.Brand));

            // multi-select: the brand facet is counted without the brand filter
            Assert.Equal(8, result.Facets["brand"].Values.Sum());
            Assert.Equal(5, result.Facets["brand"].Count);

            // the other facets follow the brand filter
            Assert.Equal(2, result.Facets["color"].Values.Sum());
            Assert.Equal(2, result.Facets["price"].Values.Sum());
        }

        [SkippableTheory]
        [MemberData(nameof(Sites))]
        public async Task Values_within_a_facet_are_ORed(string site)
        {
            var result = await Search(site, Q("laptop") + "&brand=Fabrikam&brand=Northwind");

            Assert.Equal(4, result.Total);
        }

        [SkippableTheory]
        [MemberData(nameof(Sites))]
        public async Task Price_ranges_filter_and_count(string site)
        {
            var result = await Search(site, Q("laptop") + "&price=" + Uri.EscapeDataString("1000+"));

            Assert.Equal(ProductDataset.Products.Count(p => p.Category == "Laptops" && p.Price >= 1000), result.Total);
            Assert.All(result.Hits, h => Assert.True(h.Price >= 1000));
            Assert.Equal(8, result.Facets["price"].Values.Sum()); // price facet counted without the price filter
        }

        [SkippableTheory]
        [MemberData(nameof(Sites))]
        public async Task Facets_are_ANDed(string site)
        {
            var result = await Search(site, "category=Cameras&color=Black");

            var expected = ProductDataset.Products.Where(p => p.Category == "Cameras" && p.Color == "Black").Select(p => p.Name).OrderBy(n => n);
            Assert.Equal(expected, result.Hits.Select(h => h.Name).OrderBy(n => n));
        }

        [SkippableTheory]
        [InlineData("cms11", "ait laptop 13", "Northwind Air Laptop 13")]     // typo in a three letter word
        [InlineData("cms11", "fabrikam camra", "Fabrikam Action Camera 4K")]
        [InlineData("cms12", "ait laptop 13", "Northwind Air Laptop 13")]
        [InlineData("cms12", "fabrikam camra", "Fabrikam Action Camera 4K")]
        public async Task Typos_are_forgiven(string site, string query, string expected)
        {
            var result = await Search(site, Q(query));

            Assert.Contains(expected, result.Hits.Select(h => h.Name));
        }
    
        // --- Swedish: Find's Swedish analyzer decompounds (and stems) at index time on the .sv fields. Searching with
        //     Language.Swedish finds parts of compound words - the main query and, since 3.0, FuzzyMatch/WildcardMatch
        //     search the language field too. With Language.English (no decompounding) the parts are not found.

        [SkippableTheory]
        [InlineData("cms11", "kopp", "Kaffekopp i porslin")]
        [InlineData("cms11", "väska", "Datorväska 15 tum")]
        [InlineData("cms11", "maskin", "Diskmaskin integrerad")]
        [InlineData("cms11", "maskin", "Tvättmaskin 8 kg")]
        [InlineData("cms11", "lampa", "Skrivbordslampa LED")]
        [InlineData("cms12", "kopp", "Kaffekopp i porslin")]
        [InlineData("cms12", "väska", "Datorväska 15 tum")]
        [InlineData("cms12", "maskin", "Diskmaskin integrerad")]
        [InlineData("cms12", "maskin", "Tvättmaskin 8 kg")]
        [InlineData("cms12", "lampa", "Skrivbordslampa LED")]
        public async Task Swedish_finds_parts_of_compound_words_English_does_not(string site, string part, string compound)
        {
            var swedish = await Search(site, Q(part) + "&lang=sv");
            var english = await Search(site, Q(part) + "&lang=en");

            Assert.Contains(compound, swedish.Hits.Select(h => h.Name));
            Assert.DoesNotContain(compound, english.Hits.Select(h => h.Name));
        }

        [SkippableTheory]
        [MemberData(nameof(Sites))]
        public async Task Swedish_stems_plurals_of_compound_parts(string site)
        {
            var result = await Search(site, Q("koppar") + "&lang=sv");

            Assert.Contains("Kaffekopp i porslin", result.Hits.Select(h => h.Name));
        }

        [SkippableTheory]
        [MemberData(nameof(Sites))]
        public async Task Swedish_characters_work(string site)
        {
            var result = await Search(site, Q("hörlurar") + "&lang=sv");

            Assert.Contains("Trådlösa hörlurar", result.Hits.Select(h => h.Name));
        }
}
}
