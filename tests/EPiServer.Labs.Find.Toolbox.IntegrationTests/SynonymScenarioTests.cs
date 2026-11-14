using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EPiServer.Labs.Find.Toolbox.IntegrationTests
{
    /// <summary>
    /// Runs real searches against the seeded Alloy sites and asserts exactly which test pages are returned.
    /// Documents (see sites/Shared/ToolboxTestHarness/TestDataset.cs):
    ///   D1 Episerver Find is fast            D6 Pizza in New York
    ///   D2 Search &amp; Navigation from Episerver  D7 Best NYC pizza
    ///   D3 Find your way home                D8 York pizza recipes
    ///   D4 Episerver CMS editing             D9 New pizza menu
    ///   D5 Search tips for gardeners         D10 Color palette guide
    /// Synonyms: find => "search &amp; navigation", "new york" &lt;=> nyc, Colour => Color
    /// </summary>
    public class SynonymScenarioTests
    {
        private readonly ITestOutputHelper _output;

        public SynonymScenarioTests(ITestOutputHelper output) => _output = output;

        public class Scenario
        {
            public Scenario(string name, string q, string options, params string[] expected)
            {
                Name = name; Q = q; Options = options; Expected = expected;
            }

            public string Name { get; }
            public string Q { get; }
            /// <summary>Endpoint options, e.g. "mode=msm&amp;msm=100%".</summary>
            public string Options { get; }
            public string[] Expected { get; }

            public override string ToString() => Name;
        }

        public static readonly Scenario[] Scenarios =
        {
            // --- MinimumShouldMatch + synonyms: a synonym group counts as ONE clause
            new Scenario("msm 100%: term + synonym group", "episerver find", "mode=msm&msm=100%", "D1", "D2"),
            new Scenario("msm 100%: MinimumShouldMatch after UsingSynonymsImproved", "episerver find", "mode=msm&msm=100%&msmAfterSynonyms=true", "D1", "D2"),
            new Scenario("msm 2: two clauses both required", "episerver find", "mode=msm&msm=2", "D1", "D2"),
            new Scenario("msm 1: any clause", "episerver find", "mode=msm&msm=1", "D1", "D2", "D3", "D4"),
            new Scenario("msm 75%: 3 of 4 clauses, synonym group is one clause", "episerver find editing fast", "mode=msm&msm=75%", "D1"),
            new Scenario("msm 100%: query is a single synonym group", "find", "mode=msm&msm=100%", "D1", "D2", "D3"),
            new Scenario("msm 100%: without synonyms", "episerver find", "mode=msm&msm=100%&synonyms=false", "D1"),

            // --- AND / OR + synonyms
            new Scenario("and: term + synonym group", "episerver find", "mode=and", "D1", "D2"),
            new Scenario("and: single synonym group", "find", "mode=and", "D1", "D2", "D3"),
            new Scenario("or: synonym group needs all its terms (not D5 'search' alone)", "episerver find", "mode=or", "D1", "D2", "D3", "D4"),

            // --- Multi term phrases, bidirectional
            new Scenario("bidirectional: synonym -> phrase", "nyc pizza", "mode=msm&msm=100%", "D6", "D7"),
            new Scenario("bidirectional: phrase -> synonym", "new york pizza", "mode=msm&msm=100%", "D6", "D7"),
            new Scenario("phrase with msm 2<60%", "new york pizza", "mode=msm&msm=2<60%", "D6", "D7"),
            new Scenario("phrase alone", "new york", "mode=msm&msm=100%", "D6", "D7"),

            // --- Case insensitive synonyms
            new Scenario("mixed case synonym from the Find UI", "colour palette", "mode=msm&msm=100%", "D10"),

            // --- Full README chains must not loosen MinimumShouldMatch
            new Scenario("full chain: relevance + fuzzy + wildcard", "episerver find", "mode=msm&msm=100%&relevance=true&fuzzy=true&wildcard=true", "D1", "D2"),
            new Scenario("InField: Name + SearchText", "episerver find", "mode=msm&msm=100%&inFields=true", "D1", "D2"),
        };

        public static IEnumerable<object[]> Cases()
            => from site in SiteClient.Sites
               from unified in new[] { false, true }
               from scenario in Scenarios
               select new object[] { site, unified ? "unified" : "typed", scenario.Name };

        [SkippableTheory]
        [MemberData(nameof(Cases))]
        public async Task Returns_exactly_the_expected_pages(string site, string searchType, string scenarioName)
        {
            var scenario = Scenarios.Single(s => s.Name == scenarioName);
            var client = await SiteClient.GetSeededAsync(site);
            Skip.If(client == null, $"Set TOOLBOX_{site.ToUpperInvariant()}_URL to run against the {site} site.");

            var parameters = Parse(scenario.Options);
            parameters["q"] = scenario.Q;
            parameters["unified"] = (searchType == "unified").ToString().ToLowerInvariant();

            var response = await client.SearchAsync(parameters);
            _output.WriteLine($"{site}/{searchType} '{scenario.Q}' {scenario.Options}");
            _output.WriteLine("hits:  " + string.Join(", ", response.Hits));
            _output.WriteLine("query: " + response.Query);

            Assert.Equal(scenario.Expected.OrderBy(k => k), response.Hits.OrderBy(k => k));
        }

        [SkippableTheory]
        [InlineData("cms11")]
        [InlineData("cms12")]
        public async Task Exact_phrase_match_ranks_first(string site)
        {
            var client = await SiteClient.GetSeededAsync(site);
            Skip.If(client == null, $"Set TOOLBOX_{site.ToUpperInvariant()}_URL to run against the {site} site.");

            var response = await client.SearchAsync(new Dictionary<string, string>
            {
                ["q"] = "episerver find",
                ["mode"] = "msm",
                ["msm"] = "100%",
                ["relevance"] = "true",
                ["unified"] = "true",
            });

            _output.WriteLine("hits: " + string.Join(", ", response.Hits));
            Assert.Equal("D1", response.Hits.First());
        }

        private static Dictionary<string, string> Parse(string options)
            => options.Split('&', System.StringSplitOptions.RemoveEmptyEntries)
                      .Select(o => o.Split('=', 2))
                      .ToDictionary(kv => kv[0], kv => kv.Length > 1 ? kv[1] : "true");
    }
}
