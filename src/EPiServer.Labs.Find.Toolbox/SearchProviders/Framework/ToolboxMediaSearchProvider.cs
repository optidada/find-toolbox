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
            => ToolboxSearchProviderQuery.Search<MediaData>(query, _uiDescriptorRegistry, () => _contentTypeRepository.List().Cast<ContentType>(), ServiceLocator.Current.GetInstance<IContentLoader>(),
                    addContentSpecificFields: q => AddContentSpecificFields(q))
                .Select(content => CreateSearchResult(content))
                .ToList();
    }
}
