using EPiServer.Core;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Cms;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EPiServer.Labs.Find.Toolbox.SearchProviders
{
    /// <summary>
    /// The toolbox query used by all CMS UI search providers (pages, media, blocks; CMS 11 and CMS 12).
    /// </summary>
    internal static class ToolboxSearchProviderQuery
    {
        /// <summary>Max results: the smaller of what the UI asks for and <see cref="FindToolboxOptions.SearchProviderMaxResults"/>.</summary>
        public static int MaxResults(int requested)
        {
            var max = Math.Max(1, FindToolboxOptions.Current.SearchProviderMaxResults);
            return requested > 0 ? Math.Min(requested, max) : max;
        }

        /// <summary>
        /// Builds the field query. Fields: Name (10), SearchText (1), content type name (0.5), plus the content ID for numeric queries.
        /// </summary>
        public static IQueriedSearch<IContentData, QueryStringQuery> Fields(IQueriedSearch<IContentData, QueryStringQuery> query, string searchQuery)
        {
            query = query.InField(x => ((IContent)x).Name, 10)
                         .InField(x => x.SearchText(), 1)
                         .InField(x => ((IContent)x).ContentTypeName(), 0.5);

            if (int.TryParse(searchQuery, out _))
            {
                query = query.InField(x => ((IContent)x).ContentLink.ID, 20);
            }

            return query;
        }

        /// <summary>
        /// MinimumShouldMatch, synonyms, relevance (Name), wildcard and fuzzy (Name, content type name).
        /// </summary>
        public static IQueriedSearch<IContentData, QueryStringQuery> Toolbox(IQueriedSearch<IContentData, QueryStringQuery> query)
        {
            var options = FindToolboxOptions.Current;
            if (!string.IsNullOrEmpty(options.SearchProviderMinimumShouldMatch))
            {
                query = query.MinimumShouldMatch(options.SearchProviderMinimumShouldMatch);
            }

            return query
                .UsingSynonymsImproved()
                // Phrase / phrase prefix boosts only on the name: "starts with" on the full text is costly and rarely meaningful
                .UsingRelevanceImproved(x => ((IContent)x).Name)
                .WildcardMatch(x => ((IContent)x).Name, x => ((IContent)x).ContentTypeName())
                .FuzzyMatch(x => ((IContent)x).Name, x => ((IContent)x).ContentTypeName());
        }

        /// <summary>Loads the content for each hit, skipping hits that no longer exist or are not of type <typeparamref name="TContent"/>.</summary>
        public static IEnumerable<TContent> Load<TContent>(IContentLoader contentLoader, IEnumerable<ContentInLanguageReference> hits)
            where TContent : IContentData
        {
            foreach (var hit in hits)
            {
                var selector = !string.IsNullOrEmpty(hit.Language)
                    ? new LanguageSelector(hit.Language)
                    : LanguageSelector.AutoDetect(true);

                if (contentLoader.TryGet(hit.ContentLink, selector, out TContent content))
                {
                    yield return content;
                }
            }
        }
    }
}
