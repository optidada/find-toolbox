using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using System.ComponentModel.DataAnnotations;
#if NETFRAMEWORK
using AlloyContainer = AlloyCms11.Models.Pages.ContainerPage;
#else
using AlloyContainer = AlloyCms12.Models.Pages.ContainerPage;
#endif

namespace ToolboxTestHarness.ProductDemo
{
    /// <summary>A product for the Find Toolbox faceted search demo (/product-demo). Seeded by /toolbox-test/seed.</summary>
    [ContentType(GUID = "4C7E2A61-3B0D-4C69-9E7B-1F2D3A4B5C61", DisplayName = "Find Toolbox demo product", Description = "Demo product for the Find Toolbox product search.")]
    [AvailableContentTypes(Availability.None, IncludeOn = new[] { typeof(AlloyContainer) })]
    public class DemoProductPage : PageData
    {
        /// <summary>Not "Category": that would hide PageData.Category (the CMS category list).</summary>
        [Display(Order = 10, Name = "Category")]
        public virtual string ProductCategory { get; set; }

        [Display(Order = 20)]
        public virtual string Brand { get; set; }

        [Display(Order = 30)]
        public virtual string Color { get; set; }

        [Display(Order = 40)]
        public virtual double Price { get; set; }

        [Display(Order = 50)]
        public virtual string Description { get; set; }
    }
}
