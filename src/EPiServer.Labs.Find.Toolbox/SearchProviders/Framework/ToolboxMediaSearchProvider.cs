using System.Collections.Generic;
using EPiServer.Web;
using EPiServer.Shell.Search;
using EPiServer.Framework.Localization;
using EPiServer.DataAbstraction;
using EPiServer.Shell;
using EPiServer.Framework;
using EPiServer.Find;
using EPiServer.Core;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Globalization;
using System.Web.Caching;
using EPiServer.ServiceLocation;
using System;
using EPiServer.Find.Framework;
using System.Linq;
using Newtonsoft.Json.Linq;

using EPiServer.Find.Cms;
using EPiServer.Find.Cms.SearchProviders;

namespace EPiServer.Labs.Find.Toolbox.SearchProviders
{

    [SearchProvider]
    public class ToolboxMediaSearchProvider : EnterpriseMediaSearchProvider
    {

        private readonly UIDescriptorRegistry _uiDescriptorRegistry;
        private const string AllowedTypes = "allowedTypes";
        private const string RestrictedTypes = "restrictedTypes";

        public override string Area => FindContentSearchProviderConstants.MediaArea;
        public override string Category => "Find Toolbox - Media";

        public ToolboxMediaSearchProvider(LocalizationService localizationService,
                ISiteDefinitionResolver siteDefinitionResolver, IContentTypeRepository contentTypeRepository,
                UIDescriptorRegistry uiDescriptorRegistry)
                : base(localizationService, siteDefinitionResolver, contentTypeRepository, uiDescriptorRegistry)
        {

            _uiDescriptorRegistry = uiDescriptorRegistry;
        }

        public override IEnumerable<SearchResult> Search(Query query)
        {
            Validator.ThrowIfNull("SearchProviderFactory.Instance.AccessFilter", FilterFactory.Instance.ContentAccessFilter);
            Validator.ThrowIfNull("SearchProviderFactory.Instance.CultureFilter", FilterFactory.Instance.CultureFilter);
            Validator.ThrowIfNull("SearchProviderFactory.Instance.RootsFilter", FilterFactory.Instance.RootsFilter);

            query.MaxResults = ToolboxSearchProviderQuery.MaxResults(query.MaxResults);

            ITypeSearch<IContentData> searchQuery = GetFieldQuery(query.SearchQuery, query.MaxResults)
                .Filter(x => x.MatchTypeHierarchy(typeof(MediaData)));

            var allowedTypes = GetContentTypesFromQuery(AllowedTypes, query);
            var restrictedTypes = GetContentTypesFromQuery(RestrictedTypes, query);

            FilterContext filterContext = FilterContext.Create<IContentData, ContentType>(query);
            searchQuery = FilterFactory.Instance.AllowedTypesFilter(searchQuery, filterContext, allowedTypes);
            searchQuery = FilterFactory.Instance.RestrictedTypesFilter(searchQuery, filterContext, restrictedTypes);
            searchQuery = FilterFactory.Instance.ContentAccessFilter(searchQuery, filterContext);
            searchQuery = FilterFactory.Instance.CultureFilter(searchQuery, filterContext);
            searchQuery = FilterFactory.Instance.RootsFilter(searchQuery, filterContext);

            var cacheSettings = ServiceLocator.Current.GetInstance<IContentCacheKeyCreator>();

            var hits = searchQuery
                .Select(x =>
                            new ContentInLanguageReference(
                                new ContentReference(((IContent)x).ContentLink.ID,
                                                     ((IContent)x).ContentLink.ProviderName),
                                ((ILocalizable)x).Language.Name));

            var cacheDuration = FindToolboxOptions.Current.SearchProviderCacheDuration;
            if (cacheDuration > TimeSpan.Zero)
            {
                hits = hits.StaticallyCacheFor(cacheDuration, new CacheDependency(null, new[] { cacheSettings.RootKeyName }));
            }

            var contentLinksWithLanguage = hits.GetResult();

            return ToolboxSearchProviderQuery.Load<MediaData>(ServiceLocator.Current.GetInstance<IContentLoader>(), contentLinksWithLanguage)
                .Select(content => CreateSearchResult(content))
                .ToList();
        }

        private new ITypeSearch<IContentData> GetFieldQuery(string SearchQuery, int maxResults)
        {

            var language = ResolveSupportedLanguageBasedOnPreferredCulture(SearchClient.Instance);

            if (String.IsNullOrEmpty(SearchQuery))
            {
                SearchQuery = "*";
            }

            var query = SearchClient.Instance.Search<IContentData>(language)
                .For(SearchQuery);

            query = ToolboxSearchProviderQuery.Fields(query, SearchQuery);

            query = AddContentSpecificFields(query);
            query = AddFindToolboxQueries(query);

            return query
                .Take(maxResults).SetTimeout(10000);
        }        

        private IEnumerable<Type> GetContentTypesFromQuery(string parameter, Query query)
        {
            if (query.Parameters.ContainsKey(parameter))
            {
                var array = query.Parameters[parameter] as JArray;
                if (array != null)
                {
                    return array.Values<string>().SelectMany(GetContentTypes);
                }
            }
            return Enumerable.Empty<Type>();
        }

        private new IEnumerable<Type> GetContentTypes(string allowedType)
        {
            var uiDescriptor = _uiDescriptorRegistry.UIDescriptors.FirstOrDefault(d => d.TypeIdentifier.Equals(allowedType, StringComparison.OrdinalIgnoreCase));
            if (uiDescriptor == null)
                return Enumerable.Empty<Type>();

            return _contentTypeRepository
                .List()
                .Where(c => uiDescriptor.ForType.IsAssignableFrom(c.ModelType))
                .Select(c => c.ModelType);
        }

        private Language ResolveSupportedLanguageBasedOnPreferredCulture(IClient client)
        {
            Language language = null;
            var preferredCulture = ContentLanguage.PreferredCulture;
            if (preferredCulture != null)
            {
                language = client.Settings.Languages.GetSupportedLanguage(preferredCulture);
            }
            language = language ?? Language.None;
            return language;
        }

        private IQueriedSearch<IContentData, QueryStringQuery> AddFindToolboxQueries(IQueriedSearch<IContentData, QueryStringQuery> query)
        {
            return ToolboxSearchProviderQuery.Toolbox(query);
        }

    }

}