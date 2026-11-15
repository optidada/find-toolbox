using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Caching;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Cms;
using EPiServer.Find.Cms.SearchProviders;
using EPiServer.Find.Framework;
using EPiServer.Framework;
using EPiServer.Framework.Localization;
using EPiServer.Globalization;
using EPiServer.ServiceLocation;
using EPiServer.Shell;
using EPiServer.Shell.Search;
using EPiServer.Web;
using Newtonsoft.Json.Linq;

namespace Optidada.SearchNavigation.Toolbox.SearchProviders
{
    /// <summary>
    /// CMS edit UI search for media (files) with the toolbox (synonyms, MinimumShouldMatch, relevance, fuzzy and wildcard).
    /// Enable it in the CMS admin search provider settings.
    /// </summary>
    [SearchProvider]
    public class ToolboxMediaSearchProvider : EnterpriseMediaSearchProvider
    {
        private readonly UIDescriptorRegistry _uiDescriptorRegistry;

        
        /// <inheritdoc />
        public override string Area => FindContentSearchProviderConstants.MediaArea;

        /// <inheritdoc />
        public override string Category => "Find Toolbox - Media";

        /// <summary>Created by the CMS (dependency injection).</summary>
        public ToolboxMediaSearchProvider(LocalizationService localizationService,
                ISiteDefinitionResolver siteDefinitionResolver, IContentTypeRepository contentTypeRepository,
                UIDescriptorRegistry uiDescriptorRegistry)
                : base(localizationService, siteDefinitionResolver, contentTypeRepository, uiDescriptorRegistry)
        {

            _uiDescriptorRegistry = uiDescriptorRegistry;
        }

        /// <inheritdoc />
        public override IEnumerable<SearchResult> Search(Query query)
            => ToolboxSearchProviderQuery.Search<MediaData>(query, _uiDescriptorRegistry, () => _contentTypeRepository.List().Cast<ContentType>(), ServiceLocator.Current.GetInstance<IContentLoader>(),
                    addContentSpecificFields: q => AddContentSpecificFields(q))
                .Select(content => CreateSearchResult(content))
                .ToList();
    }
}
