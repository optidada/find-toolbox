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
using EPiServer.Framework;
using EPiServer.Framework.Localization;
using EPiServer.Globalization;
using EPiServer.ServiceLocation;
using EPiServer.Shell;
using EPiServer.Shell.Search;
using EPiServer.Web;
using EPiServer.Web.Routing;
using Newtonsoft.Json.Linq;

namespace Optidada.SearchNavigation.Toolbox.SearchProviders
{
    /// <summary>
    /// CMS edit UI search for blocks with the toolbox (synonyms, MinimumShouldMatch, relevance, fuzzy and wildcard).
    /// Enable it in the CMS admin search provider settings.
    /// </summary>
    [SearchProvider]
    public class ToolboxBlockSearchProvider : EnterpriseBlockSearchProvider
    {
        private readonly UIDescriptorRegistry _uiDescriptorRegistry;
        private readonly IContentRepository _contentRepository;

        /// <inheritdoc />
        public override string Area => FindContentSearchProviderConstants.BlockArea;

        /// <inheritdoc />
        public override string Category => "Find Toolbox - Blocks";

        /// <summary>Created by the CMS (dependency injection).</summary>
        public ToolboxBlockSearchProvider(LocalizationService localizationService,
            ISiteDefinitionResolver siteDefinitionResolver,
            IContentTypeRepository<BlockType> contentTypeRepository,
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
            => ToolboxSearchProviderQuery.Search<BlockData>(query, _uiDescriptorRegistry, () => _contentTypeRepository.List().Cast<ContentType>(), _contentRepository,
                    addContentSpecificFields: q => AddContentSpecificFields(q))
                .Select(content => CreateSearchResult(content))
                .ToList();
    }
}
