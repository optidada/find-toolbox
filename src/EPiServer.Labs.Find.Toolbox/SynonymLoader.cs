using EPiServer.Find;
using EPiServer.Find.Cms;
using EPiServer.Find.Connection;
using EPiServer.Find.Framework.Statistics;
using EPiServer.Find.Optimizations;
using EPiServer.Find.Optimizations.Synonyms.Api;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using EPiServer.Framework.Cache;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EPiServer.Labs.Find.Toolbox
{
    [ServiceConfiguration(typeof(ISynonymProvider), Lifecycle = ServiceInstanceScope.Singleton)]
    [ServiceConfiguration(typeof(SynonymLoader), Lifecycle = ServiceInstanceScope.Singleton)]
    public class SynonymLoader : ISynonymProvider
    {
        private readonly IClient _client;
        private readonly IStatisticTagsHelper _statisticTagsHelper;
        private readonly IObjectInstanceCache _cache;
        private readonly FindToolboxOptions _options;
        private static readonly ILogger log = LogManager.GetLogger(typeof(SynonymLoader));

        public SynonymLoader(IClient client, IStatisticTagsHelper statisticTagsHelper, IObjectInstanceCache cache, FindToolboxOptions options)
        {
            _client = client;
            _statisticTagsHelper = statisticTagsHelper;
            _cache = cache;
            _options = options ?? FindToolboxOptions.Defaults;
        }

        public Dictionary<string, HashSet<string>> GetSynonyms(TimeSpan? cacheDuration = null)
        {
            return GetSynonyms(100, cacheDuration ?? _options.SynonymCacheDuration);
        }

        public Dictionary<string, HashSet<string>> GetSynonyms(int synonymBatchSize, TimeSpan cacheDuration)
        {
            var statisticLanguageTags = GetStatisticLanguageTags().ToList();
            string synonymCacheKey = GetSynonymCacheKey(statisticLanguageTags);

            if (_cache.Get(synonymCacheKey) is Dictionary<string, HashSet<string>> synonymsCached)
            {
                log.Debug("Loaded {0} synonyms from cache for tags {1}", synonymsCached.Count, string.Join(",", statisticLanguageTags));
                return synonymsCached;
            }

            if (!HasAdminIndex())
            {
                var empty = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                Cache(synonymCacheKey, empty, Min(cacheDuration, _options.SynonymFailureCacheDuration));
                return empty;
            }

            var loadedSynonyms = LoadSynonymsFromSourceIndex(synonymBatchSize, statisticLanguageTags, out var failed);
            var synonymDictionary = CreateSynonymDictionary(loadedSynonyms);

            // A failed (partial) load is only cached briefly so a hiccup in Find does not disable synonyms for long
            var duration = failed ? Min(cacheDuration, _options.SynonymFailureCacheDuration) : cacheDuration;
            Cache(synonymCacheKey, synonymDictionary, duration);

            log.Debug("Loaded {0} synonyms from index (cache duration: {1}, failed: {2}) for tags {3}", loadedSynonyms.Count, duration, failed, string.Join(",", statisticLanguageTags));
            return synonymDictionary;
        }

        private void Cache(string key, Dictionary<string, HashSet<string>> synonyms, TimeSpan duration)
        {
            if (duration > TimeSpan.Zero)
            {
                _cache.Insert(key, synonyms, new CacheEvictionPolicy(duration, CacheTimeoutType.Absolute));
            }
        }

        private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        private bool HasAdminIndex()
        {
            try
            {
                if (_client.Settings.Admin)
                {
                    return true;
                }

                log.Warning("Your index lacks an admin index, synonyms can not be loaded. Please contact support.");
            }
            catch (Exception ex)
            {
                log.Error("Could not read index settings, synonyms will not be used.", ex);
            }

            return false;
        }

        private List<Synonym> LoadSynonymsFromSourceIndex(int synonymBatchSize, IEnumerable<string> statisticLanguageTags, out bool failed)
        {
            var synonyms = new List<Synonym>();
            var attempt = 0;
            var page = 0;
            failed = false;

            while (true)
            {
                try
                {
                    var result = _client.Optimizations().Synonyms().List(synonymBatchSize, page * synonymBatchSize, statisticLanguageTags);
                    var hits = result.Hits.ToList();
                    synonyms.AddRange(hits);
                    if (hits.Count < synonymBatchSize) // Not a full batch indicates last batch
                    {
                        break;
                    }
                    page++;
                    attempt = 0;
                }
                catch (Exception ex)
                {
                    attempt++;
                    log.Warning(string.Format("Failed to load synonyms (attempt {0} of {1})", attempt, _options.SynonymLoadAttempts), ex);
                    if (attempt >= Math.Max(1, _options.SynonymLoadAttempts))
                    {
                        failed = true;
                        break;
                    }

                    System.Threading.Thread.Sleep(TimeSpan.FromTicks(_options.SynonymRetryDelay.Ticks * attempt));
                }
            }

            return synonyms;
        }

        /// <summary>
        /// Get current language and all-languages languages to filter and skip SiteID as it's not stored with synonyms
        /// </summary>
        /// <returns></returns>
        private IEnumerable<string> GetStatisticLanguageTags()
        {
            var statTags = _statisticTagsHelper.GetTags(true);
            return statTags.Where(x => x.Contains("language"));
        }

        private string GetSynonymCacheKey(IEnumerable<string> statisticLanguageTags)
        {
            return string.Format("FindSynonymList_" + string.Join(",", statisticLanguageTags));
        }


        private IEnumerable<Synonym> LoadSynonymsFromSourceIndex(int synonymBatchSize, IEnumerable<string> statisticLanguageTags)
        {
            var synonyms = new List<Synonym>();
            int retries = 0;
            var page = 0;
            while (true)
            {
                try
                {
                    var result = _client.Optimizations().Synonyms().List(synonymBatchSize, page * synonymBatchSize, statisticLanguageTags);
                    var hits = result.Hits.Count();
                    if (hits > 0) { synonyms.AddRange(result.Hits); }
                    if (hits < synonymBatchSize) // Not a full batch indicates last batch
                    {
                        break;
                    }
                    page++;
                    retries = 0;
                }
                catch (Exception ex)
                {
                    //Attempt a few retries
                    retries++;
                    log.Warning(string.Format("Failed to load synonyms (attempt {0})", retries), ex);
                    if (retries > 2) // Bail out if on 3 failed attempts
                    {
                        break;
                    }
                }
            }

            return synonyms;
        }

        /// <summary>
        /// Here we flatten and simplify the synonym list structure
        /// For every multiple phrase (terms separated by comma) we generate a new synonym pair
        /// For every birectional we generate a new synonym pair reversed
        /// For every multiple term we generate a new quoted variant
        /// </summary>
        /// <returns></returns>
        internal static Dictionary<string, HashSet<string>> CreateSynonymDictionary(IEnumerable<Synonym> loadedSynonyms)
        {
            var synonymsFlattened = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var synonym in loadedSynonyms)
            {

                var multiplePhrases = synonym.Phrase.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

                foreach (var singlePhrase in multiplePhrases)
                {
                    AddSynonym(singlePhrase, synonym.SynonymPhrase, ref synonymsFlattened);

                    // If multiple terms, add a new pair as a quoted phrase
                    if (ContainsMultipleTerms(singlePhrase))
                    {
                        AddSynonym(string.Format("\"{0}\"", singlePhrase), synonym.SynonymPhrase, ref synonymsFlattened);
                    }

                    // Birectional synonym, add a new pair reversed 
                    if (synonym.Bidirectional)
                    {
                        AddSynonym(synonym.SynonymPhrase, singlePhrase, ref synonymsFlattened);

                        // If multiple terms, add a new pair as a quoted phrase
                        if (ContainsMultipleTerms(synonym.SynonymPhrase))
                        {
                            AddSynonym(string.Format("\"{0}\"", synonym.SynonymPhrase), singlePhrase, ref synonymsFlattened);
                        }
                    }

                }

            }

            return synonymsFlattened;
        }

        private static void AddSynonym(string? rawPhrase, string? rawSynonym, ref Dictionary<string, HashSet<string>> synonyms)
        {
            var phrase = rawPhrase?.Trim().ToLowerInvariant();
            var synonym = rawSynonym?.Trim().ToLowerInvariant();
            if (phrase == null || synonym == null || phrase.Length == 0 || synonym.Length == 0 || phrase == synonym)
            {
                return;
            }

            if (!synonyms.TryGetValue(phrase, out var existingSynonyms))
            {
                existingSynonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                synonyms.Add(phrase, existingSynonyms);
            }

            existingSynonyms.Add(synonym);
        }

        private static bool ContainsMultipleTerms(string text)
        {
            return text.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries).Count() > 1;
        }
    }
}