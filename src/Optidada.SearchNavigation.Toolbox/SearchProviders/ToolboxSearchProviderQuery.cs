using System;
using System.Collections.Generic;
using System.Linq;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Cms;
using EPiServer.Find.Cms.SearchProviders;
using EPiServer.Find.Framework;
using EPiServer.Find.UnifiedSearch;
using EPiServer.Framework;
using EPiServer.Globalization;
using EPiServer.ServiceLocation;
using EPiServer.Shell;
using EPiServer.Shell.Search;
using Newtonsoft.Json.Linq;

namespace Optidada.SearchNavigation.Toolbox.SearchProviders
{
    /// <summary>
    /// Everything the CMS UI search providers (pages, media, blocks; CMS 11 and CMS 12) have in common. The provider classes
    /// only differ in their base class, constructor and content type.
    /// </summary>
    internal static class ToolboxSearchProviderQuery
    {
        private const string AllowedTypes = "allowedTypes";
        private const string RestrictedTypes = "restrictedTypes";

        /// <summary>
        /// Runs a provider search the way the CMS edit UI expects it: toolbox query, the provider's content type, the UI's
        /// allowed/restricted types, access, culture and roots filters; returns the loaded content.
        /// </summary>
        /// <param name="query">The edit UI query.</param>
        /// <param name="uiDescriptorRegistry">The UI descriptors (allowed/restricted types).</param>
        /// <param name="listContentTypes">The content types the provider searches.</param>
        /// <param name="contentLoader">Loads the found content.</param>
        /// <param name="isPageSearch">Pages also match absolute URLs and publish dates.</param>
        /// <param name="addContentSpecificFields">The base provider's AddContentSpecificFields (media, blocks).</param>
        public static List<TContent> Search<TContent>(
            Query query,
            UIDescriptorRegistry uiDescriptorRegistry,
            Func<IEnumerable<ContentType>> listContentTypes,
            IContentLoader contentLoader,
            bool isPageSearch = false,
            Func<IQueriedSearch<IContentData, QueryStringQuery>, IQueriedSearch<IContentData, QueryStringQuery>>? addContentSpecificFields = null)
            where TContent : IContentData
        {
            Validator.ThrowIfNull("SearchProviderFactory.Instance.AccessFilter", FilterFactory.Instance.ContentAccessFilter);
            Validator.ThrowIfNull("SearchProviderFactory.Instance.CultureFilter", FilterFactory.Instance.CultureFilter);
            Validator.ThrowIfNull("SearchProviderFactory.Instance.RootsFilter", FilterFactory.Instance.RootsFilter);

            query.MaxResults = MaxResults(query.MaxResults);

            IEnumerable<Type> ContentTypes(string parameter) => ContentTypesFromQuery(query, parameter, uiDescriptorRegistry, listContentTypes);

            var search = FieldQuery(query.SearchQuery, query.MaxResults, isPageSearch, addContentSpecificFields)
                .Filter(x => x.MatchTypeHierarchy(typeof(TContent)));

            var filterContext = FilterContext.Create<IContentData, ContentType>(query);
            search = FilterFactory.Instance.AllowedTypesFilter(search, filterContext, ContentTypes(AllowedTypes));
            search = FilterFactory.Instance.RestrictedTypesFilter(search, filterContext, ContentTypes(RestrictedTypes));
            search = FilterFactory.Instance.ContentAccessFilter(search, filterContext);
            search = FilterFactory.Instance.CultureFilter(search, filterContext);
            search = FilterFactory.Instance.RootsFilter(search, filterContext);

            var hits = search.Select(x => new ContentInLanguageReference(
                new ContentReference(((IContent)x).ContentLink.ID, ((IContent)x).ContentLink.ProviderName),
                ((ILocalizable)x).Language.Name));

            var cacheDuration = ToolboxOptions.Current.SearchProviderCacheDuration;
            if (cacheDuration > TimeSpan.Zero)
            {
                var rootKey = ServiceLocator.Current.GetInstance<IContentCacheKeyCreator>().RootKeyName;
#if NETFRAMEWORK
                hits = hits.StaticallyCacheFor(cacheDuration, new System.Web.Caching.CacheDependency(null, new[] { rootKey }));
#else
                hits = hits.StaticallyCacheFor(cacheDuration, null, rootKey);
#endif
            }

            // The CMS UI search provider API is synchronous (ISearchProvider.Search), so the synchronous Find call is intended.
#pragma warning disable CS0618
            var references = hits.GetResult();
#pragma warning restore CS0618

            return Load<TContent>(contentLoader, references).ToList();
        }

        /// <summary>Max results: the smaller of what the UI asks for and <see cref="ToolboxOptions.SearchProviderMaxResults"/>.</summary>
        public static int MaxResults(int requested)
        {
            var max = Math.Max(1, ToolboxOptions.Current.SearchProviderMaxResults);
            return requested > 0 ? Math.Min(requested, max) : max;
        }

        private static ITypeSearch<IContentData> FieldQuery(string searchQuery, int maxResults, bool isPageSearch,
            Func<IQueriedSearch<IContentData, QueryStringQuery>, IQueriedSearch<IContentData, QueryStringQuery>>? addContentSpecificFields)
        {
            var client = SearchClient.Instance;
            if (string.IsNullOrEmpty(searchQuery))
            {
                searchQuery = "*";
            }

            var query = client.Search<IContentData>(ResolveLanguage(client)).For(searchQuery);

            if (isPageSearch && IsAbsoluteUrl(searchQuery))
            {
                return query.InField(x => ((ISearchContent)x).SearchHitUrl).Take(maxResults);
            }

            query = Fields(query, searchQuery);

            if (isPageSearch && DateTime.TryParse(searchQuery, out _))
            {
                query = query.InField(x => ((ISearchContent)x).SearchPublishDate.ToString(), 3);
            }

            if (addContentSpecificFields != null)
            {
                query = addContentSpecificFields(query);
            }

            return Toolbox(query).Take(maxResults).SetTimeout(10000);
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
            var options = ToolboxOptions.Current;
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

        /// <summary>The content types the UI asks for ("allowedTypes" / "restrictedTypes" are UI descriptor type identifiers).</summary>
        private static IEnumerable<Type> ContentTypesFromQuery(Query query, string parameter, UIDescriptorRegistry uiDescriptorRegistry, Func<IEnumerable<ContentType>> listContentTypes)
        {
            if (!query.Parameters.ContainsKey(parameter) || !(query.Parameters[parameter] is JArray array))
            {
                return Enumerable.Empty<Type>();
            }

            return array.Values<string>().SelectMany(identifier =>
            {
                var uiDescriptor = uiDescriptorRegistry.UIDescriptors.FirstOrDefault(d => d.TypeIdentifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
                return uiDescriptor == null
                    ? Enumerable.Empty<Type>()
                    : listContentTypes().Where(c => uiDescriptor.ForType.IsAssignableFrom(c.ModelType)).Select(c => c.ModelType);
            }).ToList();
        }

        private static Language ResolveLanguage(IClient client)
        {
            var preferredCulture = ContentLanguage.PreferredCulture;
            return (preferredCulture != null ? client.Settings.Languages.GetSupportedLanguage(preferredCulture) : null) ?? Language.None;
        }

        private static bool IsAbsoluteUrl(string text)
            => Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
