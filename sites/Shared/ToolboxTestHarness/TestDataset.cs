using System.Collections.Generic;

namespace ToolboxTestHarness
{
    /// <summary>
    /// Deterministic test data seeded into the Alloy sites, and the expected results per scenario.
    /// Shared (linked) by the CMS 11 site, the CMS 12 site and the integration tests.
    /// No CMS / Find dependencies on purpose.
    /// </summary>
    public static class TestDataset
    {
        public const string ContainerName = "Toolbox test data";
        public const string RelevanceContainerName = "Toolbox relevance data";
        public const string Language = "en";

        public class TestDocument
        {
            public TestDocument(string key, string title) { Key = key; Title = title; }
            public string Key { get; }
            /// <summary>Used as page name and main body.</summary>
            public string Title { get; }
        }

        public class TestSynonym
        {
            public TestSynonym(string phrase, string synonym, bool bidirectional) { Phrase = phrase; Synonym = synonym; Bidirectional = bidirectional; }
            public string Phrase { get; }
            public string Synonym { get; }
            public bool Bidirectional { get; }
        }

        public static readonly TestDocument[] Documents =
        {
            new TestDocument("D1",  "Episerver Find is fast"),
            new TestDocument("D2",  "Search & Navigation from Episerver"),
            new TestDocument("D3",  "Find your way home"),
            new TestDocument("D4",  "Episerver CMS editing"),
            new TestDocument("D5",  "Search tips for gardeners"),
            new TestDocument("D6",  "Pizza in New York"),
            new TestDocument("D7",  "Best NYC pizza"),
            new TestDocument("D8",  "York pizza recipes"),
            new TestDocument("D9",  "New pizza menu"),
            new TestDocument("D10", "Color palette guide"),
            new TestDocument("D11", "Automobile insurance basics"),
        };

        /// <summary>
        /// Ranking test data (seeded in its own container so the exact-set scenarios are unaffected).
        /// </summary>
        public static readonly TestDocument[] RelevanceDocuments =
        {
            new TestDocument("R1", "Episerver Find"),                      // exact title
            new TestDocument("R2", "Episerver Find developer guide"),      // title starts with the phrase
            new TestDocument("R3", "Developer guide to Episerver Find"),   // phrase at the end
            new TestDocument("R4", "Find more about Episerver"),           // both terms, not as a phrase
            new TestDocument("R5", "Search & Navigation for editors"),     // synonym of find only
            new TestDocument("R6", "Episerver Search & Navigation"),       // episerver + synonym phrase
            new TestDocument("R7", "Navigation menus"),                    // part of the synonym only
            new TestDocument("R8", "Episerverless architecture"),          // longer word starting with episerver (wildcard)
            new TestDocument("R9", "Episervr typo guide"),                 // typo of episerver (fuzzy)
        };

        public static IEnumerable<TestDocument> AllDocuments => System.Linq.Enumerable.Concat(Documents, RelevanceDocuments);

        public static readonly TestSynonym[] Synonyms =
        {
            // one way, multi term synonym
            new TestSynonym("find", "search & navigation", false),
            // bidirectional, multi term phrase
            new TestSynonym("new york", "nyc", true),
            // mixed case as an editor might type it in the Find UI
            new TestSynonym("Colour", "Color", false),
            // the simplest case: one word to one word, lower case
            new TestSynonym("car", "automobile", false),
        };
    }

    /// <summary>The query options understood by the /toolbox-test/search endpoint.</summary>
    public class ToolboxSearchRequest
    {
        public string Q { get; set; }
        /// <summary>or | and | msm</summary>
        public string Mode { get; set; } = "or";
        /// <summary>Value for MinimumShouldMatch() when Mode == msm.</summary>
        public string Msm { get; set; }
        public bool Synonyms { get; set; } = true;
        /// <summary>Call MinimumShouldMatch() after UsingSynonymsImproved() instead of before.</summary>
        public bool MsmAfterSynonyms { get; set; }
        public bool Relevance { get; set; }
        public bool Fuzzy { get; set; }
        public bool Wildcard { get; set; }
        /// <summary>Restrict to Name + SearchText() via InField() (default: all fields).</summary>
        public bool InFields { get; set; }
        /// <summary>Use UnifiedSearch + SearchTitle (as in the README) instead of a typed Search&lt;ArticlePage&gt;.</summary>
        public bool Unified { get; set; }
        // Optional per-request FindToolboxOptions boost overrides (for tuning; test sites only)
        public double? SynonymBoost { get; set; }
        public double? SynonymVariationBoostFactor { get; set; }
        public double? PhraseBoost { get; set; }
        public double? PhrasePrefixBoost { get; set; }
        public double? PrefixBoost { get; set; }
        public double? FuzzyBoost { get; set; }
        public double? WildcardBoost { get; set; }

        /// <summary>Use Find's built-in .UsingSynonyms() instead of .UsingSynonymsImproved() (for comparison).</summary>
        public bool BuiltinSynonyms { get; set; }

        /// <summary>scenarios (D1..D10, default) | relevance (R1..R9)</summary>
        public string Set { get; set; } = "scenarios";
    }

    public class ToolboxSearchResponse
    {
        /// <summary>Document keys (D1, D2, ...) in score order.</summary>
        public List<string> Hits { get; set; } = new List<string>();
        /// <summary>Score per hit (same order as Hits).</summary>
        public List<double> Scores { get; set; } = new List<double>();
        /// <summary>The "query" part of the request sent to Find.</summary>
        public string Query { get; set; }
        public string Site { get; set; }
    }
}
