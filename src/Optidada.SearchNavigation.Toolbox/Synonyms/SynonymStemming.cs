using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EPiServer.Find;
using Optidada.SearchNavigation.Toolbox.Queries;

namespace Optidada.SearchNavigation.Toolbox.Synonyms
{
    /// <summary>
    /// Snowball stemming for synonym lookups, so "notebooks" finds the synonyms of "notebook" and "koppar" those of "kopp".
    /// The stemmer follows the language of the search (Language.English, Language.Swedish, ...).
    /// </summary>
    internal static class SynonymStemming
    {
        private static readonly Dictionary<string, Func<Snowball.Stemmer>> Stemmers = new Dictionary<string, Func<Snowball.Stemmer>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = () => new Snowball.EnglishStemmer(),
            ["sv"] = () => new Snowball.SwedishStemmer(),
            ["no"] = () => new Snowball.NorwegianStemmer(),
            ["nb"] = () => new Snowball.NorwegianStemmer(),
            ["nn"] = () => new Snowball.NorwegianStemmer(),
            ["da"] = () => new Snowball.DanishStemmer(),
            ["fi"] = () => new Snowball.FinnishStemmer(),
            ["de"] = () => new Snowball.GermanStemmer(),
            ["nl"] = () => new Snowball.DutchStemmer(),
            ["fr"] = () => new Snowball.FrenchStemmer(),
            ["es"] = () => new Snowball.SpanishStemmer(),
            ["it"] = () => new Snowball.ItalianStemmer(),
            ["pt"] = () => new Snowball.PortugueseStemmer(),
            ["ru"] = () => new Snowball.RussianStemmer(),
        };

        /// <summary>
        /// A word stemmer for the language of the search (by field suffix: en, sv, ...), or null when there is no language
        /// or no stemmer for it. Stemmer instances are not thread safe: call this per search.
        /// </summary>
        public static Func<string, string>? For(Language? language)
        {
            if (language == null || language == Language.None || string.IsNullOrEmpty(language.FieldSuffix)
                || !Stemmers.TryGetValue(language.FieldSuffix, out var create))
            {
                return null;
            }

            var stemmer = create();
            return word => StemUntilStable(stemmer, word);
        }

        /// <summary>
        /// Stems until the word no longer changes (max 3 rounds): Snowball's Swedish stemmer gives datorer => dator but
        /// dator => dat, so a single round would not match "datorer" with "dator".
        /// </summary>
        private static string StemUntilStable(Snowball.Stemmer stemmer, string word)
        {
            var current = word.ToLowerInvariant();
            for (var i = 0; i < 3; i++)
            {
                var stemmed = stemmer.Stem(current);
                if (string.IsNullOrEmpty(stemmed) || stemmed == current)
                {
                    break;
                }
                current = stemmed;
            }
            return current;
        }

        /// <summary>Stems every term of a phrase; quotes are kept ("new york" stays a quoted phrase key).</summary>
        public static string StemPhrase(string phrase, Func<string, string> stem)
        {
            var quoted = QueryText.IsQuoted(phrase);
            var text = quoted ? phrase.Trim('"') : phrase;
            var stemmed = string.Join(" ", text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(stem));
            return quoted ? "\"" + stemmed + "\"" : stemmed;
        }

        // Stemmed view of a synonym dictionary, built once per dictionary instance and language
        private static readonly ConditionalWeakTable<IDictionary<string, HashSet<string>>, ConcurrentDictionary<string, Dictionary<string, HashSet<string>>>> StemmedIndexes
            = new ConditionalWeakTable<IDictionary<string, HashSet<string>>, ConcurrentDictionary<string, Dictionary<string, HashSet<string>>>>();

        internal static Dictionary<string, HashSet<string>> StemmedIndex(IDictionary<string, HashSet<string>> synonyms, string languageKey, Func<string, string> stem)
        {
            var perLanguage = StemmedIndexes.GetValue(synonyms, _ => new ConcurrentDictionary<string, Dictionary<string, HashSet<string>>>());
            return perLanguage.GetOrAdd(languageKey, _ =>
            {
                var index = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in synonyms)
                {
                    var key = StemPhrase(entry.Key, stem);
                    if (!index.TryGetValue(key, out var values))
                    {
                        values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        index[key] = values;
                    }
                    values.UnionWith(entry.Value);
                }
                return index;
            });
        }
    }
}
