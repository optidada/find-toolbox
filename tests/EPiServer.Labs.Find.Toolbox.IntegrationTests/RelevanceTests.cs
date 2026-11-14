using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ToolboxTestHarness;
using Xunit;
using Xunit.Abstractions;

namespace EPiServer.Labs.Find.Toolbox.IntegrationTests
{
    /// <summary>
    /// Ranking rules on the relevance data set (sites/Shared/ToolboxTestHarness/TestDataset.cs):
    ///   R1 Episerver Find                       R6 Episerver Search &amp; Navigation
    ///   R2 Episerver Find developer guide       R7 Navigation menus
    ///   R3 Developer guide to Episerver Find    R8 Episerverless architecture
    ///   R4 Find more about Episerver            R9 Episervr typo guide
    ///   R5 Search &amp; Navigation for editors
    /// Synonym: find => "search &amp; navigation".
    /// </summary>
    public class RelevanceTests
    {
        private const string Msm100 = "mode=msm&msm=100%";
        private const string FullChain = Msm100 + "&relevance=true&fuzzy=true&wildcard=true";

        private readonly ITestOutputHelper _output;

        public RelevanceTests(ITestOutputHelper output) => _output = output;

        public static IEnumerable<object[]> Targets()
            => from site in SiteClient.Sites
               from type in new[] { "typed", "unified" }
               select new object[] { site, type };

        private async Task<List<string>> Search(string site, string type, string q, string options)
        {
            var client = await SiteClient.GetSeededAsync(site);
            Skip.If(client == null, $"Set TOOLBOX_{site.ToUpperInvariant()}_URL to run against the {site} site.");

            var parameters = options.Split('&').Select(o => o.Split('=')).ToDictionary(kv => kv[0], kv => kv[1]);
            parameters["q"] = q;
            parameters["set"] = "relevance";
            parameters["unified"] = (type == "unified").ToString().ToLowerInvariant();

            ToolboxSearchResponse response = await client.SearchAsync(parameters);
            _output.WriteLine($"{site}/{type} '{q}' {options}");
            _output.WriteLine(string.Join("  ", response.Hits.Zip(response.Scores, (h, s) => $"{h}:{s:0.000}")));
            return response.Hits;
        }

        private static void Before(List<string> hits, string[] first, string[] then)
        {
            foreach (var a in first)
            {
                foreach (var b in then)
                {
                    Assert.True(hits.Contains(a), $"{a} missing in {string.Join(",", hits)}");
                    Assert.True(hits.Contains(b), $"{b} missing in {string.Join(",", hits)}");
                    Assert.True(hits.IndexOf(a) < hits.IndexOf(b), $"expected {a} before {b}: {string.Join(",", hits)}");
                }
            }
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Exact_title_first_then_starts_with_then_phrase_at_end(string site, string type)
        {
            var hits = await Search(site, type, "episerver find", FullChain);

            Assert.Equal("R1", hits[0]);
            Before(hits, new[] { "R2" }, new[] { "R3" });
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Phrase_beats_scattered_terms(string site, string type)
        {
            var hits = await Search(site, type, "episerver find", FullChain);

            Before(hits, new[] { "R1", "R2", "R3" }, new[] { "R4" });
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Users_phrase_beats_synonym_phrase(string site, string type)
        {
            // Before tuning: R6 (Episerver Search & Navigation) scored 7x higher than R1 (Episerver Find)
            var hits = await Search(site, type, "episerver find", FullChain);

            Before(hits, new[] { "R1", "R2", "R3" }, new[] { "R6" });
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Users_word_beats_synonym_with_relevance(string site, string type)
        {
            // Before tuning: synonym-only pages R5/R6 ranked above every page containing "find"
            var hits = await Search(site, type, "find", FullChain);

            Before(hits, new[] { "R1", "R2", "R3", "R4" }, new[] { "R5", "R6" });
            Assert.DoesNotContain("R7", hits); // "navigation" alone is not the synonym
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Users_word_beats_synonym_without_relevance(string site, string type)
        {
            var hits = await Search(site, type, "find", Msm100);

            Before(hits, new[] { "R1", "R2", "R3", "R4" }, new[] { "R5", "R6" });
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Users_terms_beat_synonym_without_relevance(string site, string type)
        {
            var hits = await Search(site, type, "episerver find", Msm100);

            Before(hits, new[] { "R1", "R2", "R3", "R4" }, new[] { "R6" });
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Same_order_with_and_operator(string site, string type)
        {
            var hits = await Search(site, type, "episerver find", "mode=and&relevance=true&fuzzy=true&wildcard=true");

            Assert.Equal("R1", hits[0]);
            Before(hits, new[] { "R1", "R2", "R3" }, new[] { "R6" });
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Ranking_is_case_insensitive(string site, string type)
        {
            var lower = await Search(site, type, "episerver find", FullChain);
            var upper = await Search(site, type, "EPISERVER FIND", FullChain);

            Assert.Equal(lower, upper);
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Exact_word_beats_fuzzy_and_wildcard_matches(string site, string type)
        {
            var hits = await Search(site, type, "episerver", FullChain);

            Before(hits, new[] { "R1", "R2", "R3", "R4", "R6" }, new[] { "R8", "R9" });
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Typo_finds_the_correct_word(string site, string type)
        {
            var hits = await Search(site, type, "episervr", FullChain);

            Assert.Equal("R9", hits[0]); // exact match on the typo itself
            Assert.Contains("R1", hits);  // FuzzyMatch: episervr ~ episerver
        }

        [SkippableTheory, MemberData(nameof(Targets))]
        public async Task Partial_word_finds_completions(string site, string type)
        {
            var hits = await Search(site, type, "episerv", FullChain);

            foreach (var key in new[] { "R1", "R2", "R3", "R4", "R6", "R8" })
            {
                Assert.Contains(key, hits); // WildcardMatch: episerv*
            }
        }
    }
}
