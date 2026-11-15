using System;
using System.Collections.Generic;
using System.Linq;
using EPiServer;
using EPiServer.Cms.Shell.UI.UIDescriptors.ViewConfigurations.Internal;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Editor;
using EPiServer.Find;
using EPiServer.Find.Api;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Cms;
using EPiServer.Find.Cms.SearchProviders;
using EPiServer.Find.Framework;
using EPiServer.Find.UnifiedSearch;
using EPiServer.Framework;
using EPiServer.Framework.Localization;
using EPiServer.Globalization;
using EPiServer.ServiceLocation;
using EPiServer.Shell;
using EPiServer.Shell.Search;
using EPiServer.Web;
using EPiServer.Web.Routing;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;

namespace Optidada.SearchNavigation.Toolbox.SearchProviders
{
    /// <summary>
    /// CMS edit UI search for pages with the toolbox (synonyms, MinimumShouldMatch, relevance, fuzzy and wildcard).
    /// Enable it in the CMS admin search provider settings.
    /// </summary>
    [SearchProvider]
    public class ToolboxPageSearchProvider : EnterprisePageSearchProvider
    {
        private readonly UIDescriptorRegistry _uiDescriptorRegistry;
        private readonly IContentRepository _contentRepository;

        
        /// <inheritdoc />
        public override string Area => FindContentSearchProviderConstants.PageArea;

        /// <inheritdoc />
        public override string Category => "Find Toolbox - Pages";

        /// <summary>Created by the CMS (dependency injection).</summary>
        public ToolboxPageSearchProvider(LocalizationService localizationService,
            ISiteDefinitionResolver siteDefinitionResolver,
            IContentTypeRepository<PageType> contentTypeRepository,
            UIDescriptorRegistry uiDescriptorRegistry,
            EditUrlResolver editUrlResolver,
            ServiceAccessor<SiteDefinition> currentSiteDefinition,
            IContentLanguageAccessor languageResolver,
            IUrlResolver urlResolver,
            ITemplateResolver templateResolver,
            IContentRepository contentRepository)
            : base(localizationService, siteDefinitionResolver, contentTypeRepository, uiDescriptorRegistry, editUrlResolver, currentSiteDefinition, languageResolver, urlResolver, templateResolver, contentRepository)
        {
            _contentRepository = contentRepository;
            _uiDescriptorRegistry = uiDescriptorRegistry;
        }

        /// <inheritdoc />
        public override IEnumerable<SearchResult> Search(Query query)
            => ToolboxSearchProviderQuery.Search<PageData>(query, _uiDescriptorRegistry, () => _contentTypeRepository.List().Cast<ContentType>(), _contentRepository,
                    isPageSearch: true)
                .Select(content => CreateSearchResult(content))
                .ToList();
    }
}
