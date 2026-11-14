using EPiServer.ServiceLocation;
using System;

namespace EPiServer.Labs.Find.Toolbox
{
    /// <summary>
    /// Limits that keep queries from expanding too much. Defaults are safe for most sites.
    /// <code>
    /// // CMS 12 (Startup.ConfigureServices) / CMS 11 (IConfigurableModule.ConfigureContainer)
    /// services.Configure&lt;FindToolboxOptions&gt;(o =&gt; o.MaxSynonymsPerPhrase = 5);
    /// </code>
    /// </summary>
    [Options]
    public class FindToolboxOptions
    {
        // ---- Query parsing

        /// <summary>Max terms (and quoted phrases) taken from the user query.</summary>
        public int MaxQueryTerms { get; set; } = 50;

        // ---- Synonyms

        /// <summary>
        /// Upper bound for the length (in terms) of synonym phrases that are looked up.
        /// The actual bound is the longest phrase in the synonym list, capped by this value.
        /// </summary>
        public int MaxSynonymPhraseTerms { get; set; } = 10;

        /// <summary>Max synonyms added for one matched phrase, e.g. (find OR s1 OR ... OR s10).</summary>
        public int MaxSynonymsPerPhrase { get; set; } = 10;

        /// <summary>Max number of phrases expanded with synonyms in one query. Later matches are left unexpanded.</summary>
        public int MaxExpandedPhrases { get; set; } = 5;

        /// <summary>Max query variations (original + synonym variations) used by UsingRelevanceImproved().</summary>
        public int MaxQueriesForMatch { get; set; } = 3;

        /// <summary>How long the synonym list is cached when UsingSynonymsImproved() is called without a duration.</summary>
        public TimeSpan SynonymCacheDuration { get; set; } = TimeSpan.FromHours(1);

        /// <summary>How long a synonym list is cached when loading from Find failed (so a hiccup does not disable synonyms for long).</summary>
        public TimeSpan SynonymFailureCacheDuration { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>Attempts per synonym batch before giving up.</summary>
        public int SynonymLoadAttempts { get; set; } = 3;

        /// <summary>Delay between synonym load attempts (multiplied by the attempt number).</summary>
        public TimeSpan SynonymRetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

        // ---- Boosts

        /// <summary>
        /// Boost of synonym alternatives inside the synonym group, normalized by length:
        /// find => (find OR fnd^0.1 OR (search AND navigation)^0.05). 1 = same as the original term.
        /// </summary>
        public double SynonymBoost { get; set; } = 0.1;

        /// <summary>Factor applied to the phrase/prefix boosts of synonym variations in UsingRelevanceImproved(). 1 = same as the original query.</summary>
        public double SynonymVariationBoostFactor { get; set; } = 0.2;

        /// <summary>match_phrase (phrase anywhere in the field) boost in UsingRelevanceImproved().</summary>
        public double PhraseBoost { get; set; } = 10;

        /// <summary>match_phrase_prefix (field starts with the phrase) boost in UsingRelevanceImproved().</summary>
        public double PhrasePrefixBoost { get; set; } = 5;

        /// <summary>prefix (field starts with the single term) boost in UsingRelevanceImproved().</summary>
        public double PrefixBoost { get; set; } = 0.5;

        /// <summary>Default boost of FuzzyMatch().</summary>
        public double FuzzyBoost { get; set; } = 0.4;

        /// <summary>Default boost of WildcardMatch().</summary>
        public double WildcardBoost { get; set; } = 0.2;

        // ---- UsingRelevanceImproved()

        /// <summary>Max terms in the match_phrase / match_phrase_prefix queries.</summary>
        public int MaxRelevanceTerms { get; set; } = 10;

        /// <summary>Min length of a single term query before a prefix query is added.</summary>
        public int MinPrefixLength { get; set; } = 3;

        // ---- FuzzyMatch() / WildcardMatch()

        /// <summary>Min length of a term to get fuzzy/wildcard matching.</summary>
        public int MinFuzzyWildcardTermLength { get; set; } = 3;

        /// <summary>Max length of a term to get fuzzy/wildcard matching.</summary>
        public int MaxFuzzyWildcardTermLength { get; set; } = 16;

        /// <summary>Max number of terms that get fuzzy/wildcard matching.</summary>
        public int MaxFuzzyWildcardTerms { get; set; } = 3;

        /// <summary>
        /// FuzzyMatch() also searches the language field (.sv, .en, ...) when the search has a language. That field holds
        /// stems and compound parts: short terms that a one-edit fuzzy match easily hits by accident.
        /// </summary>
        public bool FuzzyUseLanguageField { get; set; } = true;

        /// <summary>WildcardMatch() also searches the language field (.sv, .en, ...) when the search has a language.</summary>
        public bool WildcardUseLanguageField { get; set; } = true;

        /// <summary>
        /// Number of leading characters that must match exactly in fuzzy matching. 1 (default): "ait" finds "air",
        /// "lapotp" finds "laptop". Higher values are cheaper but never correct a typo within the first characters
        /// (upstream used 3, which made fuzzy matching start at 4 characters).
        /// </summary>
        public int FuzzyPrefixLength { get; set; } = 1;

        // ---- CMS UI search providers (Find Toolbox - Pages / Media / Blocks)

        /// <summary>MinimumShouldMatch used by the CMS UI search providers. Null or empty = not used (default operator OR).</summary>
        public string SearchProviderMinimumShouldMatch { get; set; } = "1";

        /// <summary>
        /// How long CMS UI search provider results are cached. Default: not cached, so editors immediately find
        /// content they just created (the cache was not reliably invalidated on content changes).
        /// </summary>
        public TimeSpan SearchProviderCacheDuration { get; set; } = TimeSpan.Zero;

        /// <summary>Max hits returned by the CMS UI search providers.</summary>
        public int SearchProviderMaxResults { get; set; } = 20;

        private static FindToolboxOptions _override;

        /// <summary>
        /// The registered options, or defaults when no service locator is available (e.g. unit tests).
        /// </summary>
        public static FindToolboxOptions Current
        {
            get
            {
                if (_override != null)
                {
                    return _override;
                }

                try
                {
                    if (ServiceLocator.Current != null && ServiceLocator.Current.TryGetExistingInstance(out FindToolboxOptions options))
                    {
                        return options;
                    }
                }
                catch (Exception)
                {
                    // No container configured
                }

                return Defaults;
            }
        }

        internal static readonly FindToolboxOptions Defaults = new FindToolboxOptions();

        /// <summary>For tests: use <paramref name="options"/> until the returned scope is disposed.</summary>
        internal static IDisposable Override(FindToolboxOptions options)
        {
            _override = options;
            return new OverrideScope();
        }

        private sealed class OverrideScope : IDisposable
        {
            public void Dispose() => _override = null;
        }
    }
}
