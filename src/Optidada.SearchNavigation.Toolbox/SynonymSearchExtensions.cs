using System;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using Optidada.SearchNavigation.Toolbox.Synonyms;

namespace Optidada.SearchNavigation.Toolbox
{
    /// <summary><c>UsingSynonymsImproved()</c>: synonyms that work with MinimumShouldMatch, AND and multi word phrases.</summary>
    public static class SynonymSearchExtensions
    {
        /// <summary>
        /// Expands the query with the synonyms from the Find admin UI. A term and its synonyms become one group, so they count
        /// as one term for MinimumShouldMatch and AND. Synonyms rank below the user's own words (<see cref="ToolboxOptions.SynonymBoost"/>).
        /// </summary>
        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
            => search.UsingSynonymsImproved(ToolboxServices.SynonymService, cacheDuration);

        /// <summary>Expands the query with synonyms using an explicitly given service (e.g. with your own <see cref="ISynonymProvider"/>).</summary>
        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search, SynonymService synonymService, TimeSpan? cacheDuration = null)
        {
            if (synonymService == null)
            {
                throw new ArgumentNullException(nameof(synonymService));
            }

            return synonymService.UsingSynonyms(search, cacheDuration);
        }
    }
}
