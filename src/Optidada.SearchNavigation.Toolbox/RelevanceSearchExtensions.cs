using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Logging;
using Optidada.SearchNavigation.Toolbox.Queries;

namespace Optidada.SearchNavigation.Toolbox
{
    /// <summary><c>UsingRelevanceImproved()</c>: phrase and prefix boosts.</summary>
    public static class RelevanceSearchExtensions
    {
        private static readonly ILogger Log = LogManager.GetLogger(typeof(RelevanceSearchExtensions));

        /// <summary>
        /// Boosts documents where the fields contain the query as a phrase (match_phrase), start with it
        /// (match_phrase_prefix) or, for one word, contain / start with the word. Also for the synonym variations from
        /// UsingSynonymsImproved(), with a lower boost.
        /// </summary>
        public static IQueriedSearch<TSource, QueryStringQuery> UsingRelevanceImproved<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                var main = MainQuery.Find(search, context, "UsingRelevanceImproved()");
                var options = ToolboxServices.Options;
                if (main == null || main.LeaveToFind(options))
                {
                    return;
                }

                // The user's words, or (after UsingSynonymsImproved) the user's words + synonym variations
                var queriesForMatch = main.State.QueriesForMatch is string[] variations && variations.Length > 0
                    ? variations
                    : new[] { string.Join(" ", QueryText.GetQueryPhrases(main.RawQuery, options.MaxQueryTerms)) };

                var clauses = new List<IQuery>();
                var originalTermCount = 1;
                for (var variation = 0; variation < queriesForMatch.Length; variation++)
                {
                    // match_phrase / match_phrase_prefix / prefix are not query_string queries: no escaping, no quotes.
                    // Punctuation only tokens (e.g. & in "search & navigation") are dropped: Find's shingled fields index them
                    // as tokens, which inflates phrase scores. Limit the number of terms: match_phrase_prefix expands the last term.
                    var terms = queriesForMatch[variation].Replace("\"", string.Empty)
                        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Any(char.IsLetterOrDigit))
                        .Take(Math.Max(1, options.MaxRelevanceTerms))
                        .ToArray();
                    if (terms.Length == 0)
                    {
                        continue;
                    }

                    // The first variation is the user's query; the rest are synonym variations. Synonym variations get a lower
                    // boost, normalized by length: phrase scores grow with the number of (shingled) terms, so a longer synonym
                    // would otherwise outrank the user's own words.
                    if (variation == 0)
                    {
                        originalTermCount = terms.Length;
                    }

                    var factor = variation == 0 ? 1 : options.SynonymVariationBoostFactor * Math.Min(1.0, (double)originalTermCount / terms.Length);
                    clauses.AddRange(RelevanceClauses(search.Client, fieldSelectors, terms, factor, options));
                }

                main.AddOptional(clauses.ToArray());
                Log.Debug("UsingRelevanceImproved: {0} clauses", clauses.Count);
            });
        }

        private static IEnumerable<IQuery> RelevanceClauses<TSource>(IClient client, IEnumerable<Expression<Func<TSource, string>>> fieldSelectors,
            string[] terms, double factor, ToolboxOptions options)
        {
            var query = string.Join(" ", terms);
            foreach (var fieldSelector in fieldSelectors)
            {
                var lowercaseField = client.Conventions.FieldNameConvention.GetFieldNameForLowercase(fieldSelector);
                var analyzedField = client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector);

                if (terms.Length == 1)
                {
                    // Field contains the term
                    if (terms[0].Length > 1)
                    {
                        yield return new PhraseQuery(analyzedField, query) { Boost = options.PhraseBoost * factor, Slop = options.PhraseSlop };
                    }

                    // Field starts with the term
                    if (terms[0].Length >= options.MinPrefixLength)
                    {
                        yield return new PrefixQuery(lowercaseField, query.ToLowerInvariant()) { Boost = options.PrefixBoost * factor };
                    }
                }
                else if (terms.Any(x => x.Length > 1))
                {
                    // Field contains the phrase / field starts with the phrase
                    yield return new PhraseQuery(analyzedField, query) { Boost = options.PhraseBoost * factor, Slop = options.PhraseSlop };
                    yield return new PhrasePrefixQuery(lowercaseField, query.ToLowerInvariant()) { Boost = options.PhrasePrefixBoost * factor };
                }
            }
        }
    }
}
