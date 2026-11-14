using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EPiServer.Labs.Find.Toolbox.IntegrationTests
{
    /// <summary>
    /// The CMS UI search providers ("Find Toolbox - Pages / Media / Blocks"), run the way the edit UI runs them.
    /// </summary>
    public class SearchProviderTests
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        private readonly ITestOutputHelper _output;

        public SearchProviderTests(ITestOutputHelper output) => _output = output;

        private async Task<List<string>> Provider(string site, string area, string q)
        {
            var client = await SiteClient.GetSeededAsync(site);
            Skip.If(client == null, $"Set TOOLBOX_{site.ToUpperInvariant()}_URL to run against the {site} site.");

            var url = SiteClient.GetUrl(site).TrimEnd('/') + "/toolbox-test/provider?area=" + area + "&q=" + Uri.EscapeDataString(q);
            var hits = JsonSerializer.Deserialize<List<string>>(await Http.GetStringAsync(url));
            _output.WriteLine($"{site} {area} '{q}': {string.Join(", ", hits)}");
            return hits;
        }

        [SkippableTheory]
        [InlineData("cms11")]
        [InlineData("cms12")]
        public async Task Page_provider_ranks_exact_title_first(string site)
        {
            var hits = await Provider(site, "pages", "episerver find");

            Assert.Equal("R1", hits[0]);
            Assert.Contains("R2", hits);
            Assert.Contains("D1", hits);
        }

        [SkippableTheory]
        [InlineData("cms11")]
        [InlineData("cms12")]
        public async Task Page_provider_uses_synonyms_but_ranks_them_after_the_users_word(string site)
        {
            var hits = await Provider(site, "pages", "find");

            Assert.Contains("R5", hits); // Search & Navigation for editors (synonym only)
            Assert.True(hits.IndexOf("R1") < hits.IndexOf("R5"), string.Join(",", hits));
        }

        [SkippableTheory]
        [InlineData("cms11")]
        [InlineData("cms12")]
        public async Task Page_provider_tolerates_typos(string site)
        {
            var hits = await Provider(site, "pages", "episervr");

            Assert.Contains("R1", hits);
        }

        [SkippableTheory]
        [InlineData("cms11")]
        [InlineData("cms12")]
        public async Task Block_and_media_providers_return_no_pages(string site)
        {
            Assert.Empty(await Provider(site, "blocks", "episerver find"));
            Assert.Empty(await Provider(site, "media", "episerver find"));
        }
    }
}
