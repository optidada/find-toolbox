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
using EPiServer.Web.Routing;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EPiServer.Labs.Find.Toolbox.SearchProviders
{

    [SearchProvider]
    public class ToolboxMediaSearchProvider : EnterpriseMediaSearchProvider
    {

        private readonly UIDescriptorRegistry _uiDescriptorRegistry;
        private readonly IContentRepository _contentRepository;


        public override string Area => FindContentSearchProviderConstants.MediaArea;
        public override string Category => "Find Toolbox - Media";

        public ToolboxMediaSearchProvider(LocalizationService localizationService,
            ISiteDefinitionResolver siteDefinitionResolver,
            IContentTypeRepository<ContentType> contentTypeRepository,
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
            => ToolboxSearchProviderQuery.Search<MediaData>(query, _uiDescriptorRegistry, () => _contentTypeRepository.List().Cast<ContentType>(), _contentRepository,
                    addContentSpecificFields: q => AddContentSpecificFields(q))
                .Select(content => CreateSearchResult(content))
                .ToList();
    }
}
