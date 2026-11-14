using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Framework;
using EPiServer.Framework;
using EPiServer.Framework.Localization;
using EPiServer.Globalization;
using EPiServer.ServiceLocation;
using EPiServer.Shell;
using EPiServer.Shell.Search;
using EPiServer.Web;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Caching;

using EPiServer.Find.Cms;
using EPiServer.Find.Cms.SearchProviders;

namespace EPiServer.Labs.Find.Toolbox.SearchProviders
{
    [SearchProvider]
    public class ToolboxBlockSearchProvider : EnterpriseBlockSearchProvider
    {
        private readonly UIDescriptorRegistry _uiDescriptorRegistry;

        public override string Area => FindContentSearchProviderConstants.BlockArea;
        public override string Category => "Find Toolbox - Blocks";

        public ToolboxBlockSearchProvider(LocalizationService localizationService,
                ISiteDefinitionResolver siteDefinitionResolver, IContentTypeRepository contentTypeRepository,
                UIDescriptorRegistry uiDescriptorRegistry)
                : base(localizationService, siteDefinitionResolver, contentTypeRepository, uiDescriptorRegistry)
        {
            _uiDescriptorRegistry = uiDescriptorRegistry;
        }

        public override IEnumerable<SearchResult> Search(Query query)
            => ToolboxSearchProviderQuery.Search<BlockData>(query, _uiDescriptorRegistry, () => _contentTypeRepository.List().Cast<ContentType>(), ServiceLocator.Current.GetInstance<IContentLoader>(),
                    addContentSpecificFields: q => AddContentSpecificFields(q))
                .Select(content => CreateSearchResult(content))
                .ToList();
    }
}
