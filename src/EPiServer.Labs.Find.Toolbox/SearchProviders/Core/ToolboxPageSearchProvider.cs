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
using EPiServer.ServiceLocation;
using System;
using EPiServer.Find.Framework;
using System.Linq;
using Newtonsoft.Json.Linq;
using EPiServer.Find.UnifiedSearch;
using EPiServer.Web.Routing;
using EPiServer.Find.Cms;
using EPiServer.Find.Cms.SearchProviders;
using EPiServer.Cms.Shell.UI.UIDescriptors.ViewConfigurations.Internal;
using EPiServer.Editor;
using EPiServer.Find.Api;
using Microsoft.AspNetCore.Http;

namespace EPiServer.Labs.Find.Toolbox.SearchProviders
{

    [SearchProvider]
    public class ToolboxPageSearchProvider : EnterprisePageSearchProvider
    {

        private readonly UIDescriptorRegistry _uiDescriptorRegistry;
        private readonly IContentRepository _contentRepository;

        public override string Area => FindContentSearchProviderConstants.PageArea;
        public override string Category => "Find Toolbox - Pages";

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

        public override IEnumerable<SearchResult> Search(Query query)
            => ToolboxSearchProviderQuery.Search<PageData>(query, _uiDescriptorRegistry, () => _contentTypeRepository.List().Cast<ContentType>(), _contentRepository,
                    isPageSearch: true)
                .Select(content => CreateSearchResult(content))
                .ToList();
    }
}
