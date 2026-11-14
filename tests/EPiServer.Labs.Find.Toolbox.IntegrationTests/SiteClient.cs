using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ToolboxTestHarness;

namespace EPiServer.Labs.Find.Toolbox.IntegrationTests
{
    /// <summary>
    /// Talks to the /toolbox-test endpoints of a running Alloy site.
    ///
    /// Configure sites with environment variables (unset = tests for that site are skipped):
    ///   TOOLBOX_CMS12_URL=http://localhost:5112
    ///   TOOLBOX_CMS11_URL=http://localhost:5111
    /// </summary>
    public sealed class SiteClient
    {
        public static readonly string[] Sites = { "cms11", "cms12" };

        private static readonly ConcurrentDictionary<string, Lazy<Task>> Seeded = new ConcurrentDictionary<string, Lazy<Task>>();
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly Uri _baseUri;

        private SiteClient(string site, Uri baseUri)
        {
            Site = site;
            _baseUri = baseUri;
        }

        public string Site { get; }

        public static string GetUrl(string site) => Environment.GetEnvironmentVariable("TOOLBOX_" + site.ToUpperInvariant() + "_URL");

        /// <summary>Returns a client for a seeded site, or null when the site is not configured.</summary>
        public static async Task<SiteClient> GetSeededAsync(string site)
        {
            var url = GetUrl(site);
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var client = new SiteClient(site, new Uri(url.TrimEnd('/') + "/"));
            await Seeded.GetOrAdd(site, _ => new Lazy<Task>(client.SeedAndWaitAsync)).Value;
            return client;
        }

        private async Task SeedAndWaitAsync()
        {
            // TOOLBOX_RESET_INDEX=true wipes ALL documents and synonyms in the site's index before seeding
            var reset = string.Equals(Environment.GetEnvironmentVariable("TOOLBOX_RESET_INDEX"), "true", StringComparison.OrdinalIgnoreCase);
            var response = await Http.PostAsync(new Uri(_baseUri, "toolbox-test/seed?resetIndex=" + reset.ToString().ToLowerInvariant()), null);
            response.EnsureSuccessStatusCode();

            // Wait until every seeded page is searchable (the index refreshes asynchronously)
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (true)
            {
                var count = int.Parse(await Http.GetStringAsync(new Uri(_baseUri, "toolbox-test/count")));
                if (count == TestDataset.Documents.Length)
                {
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"{Site}: expected {TestDataset.Documents.Length} seeded documents in the index, found {count}");
                }

                await Task.Delay(1000);
            }
        }

        public async Task<ToolboxSearchResponse> SearchAsync(IDictionary<string, string> parameters)
        {
            var query = string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
            var response = await Http.GetAsync(new Uri(_baseUri, "toolbox-test/search?" + query));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{Site}: {(int)response.StatusCode} {body}");
            }

            return JsonSerializer.Deserialize<ToolboxSearchResponse>(body, JsonOptions);
        }
    }
}
