using EPiServer;
using EPiServer.Find;
using EPiServer.Find.Framework;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using Optidada.SearchNavigation.Toolbox;
using Optidada.SearchNavigation.Toolbox.SearchProviders;
using EPiServer.ServiceLocation;
using EPiServer.Shell.Search;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Routing;
using ToolboxTestHarness;

namespace AlloyCms11.Business.ToolboxTest
{
    /// <summary>
    /// Test-only endpoints used by tests/Optidada.SearchNavigation.Toolbox.IntegrationTests (same API as the CMS 12 site).
    /// Only answers local requests.
    /// </summary>
    public class ToolboxTestController : Controller
    {
        private static ToolboxTestService Service => new ToolboxTestService(
            ServiceLocator.Current.GetInstance<IContentRepository>(),
            SearchClient.Instance,
            ServiceLocator.Current.GetInstance<ToolboxOptions>());

        [HttpPost]
        public async Task<ActionResult> Seed(bool resetIndex = false)
            => Request.IsLocal ? JsonContent(await Service.SeedAsync(resetIndex)) : HttpNotFound();

        [HttpPost]
        [ActionName("index-site")]
        public async Task<ActionResult> IndexSite()
            => Request.IsLocal ? JsonContent(await Service.IndexSiteAsync()) : HttpNotFound();

        [HttpGet]
        public async Task<ActionResult> Products(ToolboxTestHarness.ProductDemo.ProductSearchRequest request)
            => Request.IsLocal ? JsonContent(await Service.ProductSearchAsync(request)) : HttpNotFound();

        [HttpGet]
        public async Task<ActionResult> Count()
            => Request.IsLocal ? JsonContent(await Service.CountAsync()) : HttpNotFound();

        [HttpGet]
        public async Task<ActionResult> Search(ToolboxSearchRequest request)
            => Request.IsLocal ? JsonContent(await Service.SearchAsync(request)) : HttpNotFound();

        /// <param name="area">pages | media | blocks</param>
        [HttpGet]
        public ActionResult Provider(string area, string q)
        {
            if (!Request.IsLocal)
            {
                return HttpNotFound();
            }

            ISearchProvider provider;
            switch (area)
            {
                case "media": provider = ServiceLocator.Current.GetInstance<ToolboxMediaSearchProvider>(); break;
                case "blocks": provider = ServiceLocator.Current.GetInstance<ToolboxBlockSearchProvider>(); break;
                default: provider = ServiceLocator.Current.GetInstance<ToolboxPageSearchProvider>(); break;
            }

            return JsonContent(Service.SearchWithProvider(provider, q));
        }

        private ActionResult JsonContent(object value) => Content(JsonConvert.SerializeObject(value, new JsonSerializerSettings
        {
            // camelCase property names like ASP.NET Core, but keep dictionary keys (facet values) as they are
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
            }
        }), "application/json");
    }

    /// <summary>Faceted product search demo powered by the Find Toolbox: /product-demo?q=laptop&amp;brand=Fabrikam</summary>
    public class ProductDemoController : Controller
    {
        public async Task<ActionResult> Index(ToolboxTestHarness.ProductDemo.ProductSearchRequest request)
        {
            var service = new ToolboxTestService(
                ServiceLocator.Current.GetInstance<IContentRepository>(),
                SearchClient.Instance,
                ServiceLocator.Current.GetInstance<ToolboxOptions>());
            return View("~/Views/ProductDemo/Index.cshtml", await service.ProductSearchAsync(request ?? new ToolboxTestHarness.ProductDemo.ProductSearchRequest()));
        }
    }

    /// <summary>Routes /toolbox-test/{action} before the CMS content routes.</summary>
    [InitializableModule]
    [ModuleDependency(typeof(EPiServer.Web.InitializationModule))]
    public class ToolboxTestRouteInitialization : IInitializableModule
    {
        public void Initialize(InitializationEngine context)
        {
            RouteTable.Routes.Insert(0, new Route(
                "product-demo",
                new RouteValueDictionary(new { controller = "ProductDemo", action = "Index" }),
                new MvcRouteHandler()));
            RouteTable.Routes.Insert(0, new Route(
                "toolbox-test/{action}",
                new RouteValueDictionary(new { controller = "ToolboxTest" }),
                new MvcRouteHandler()));
        }

        public void Uninitialize(InitializationEngine context)
        {
        }
    }
}
