using EPiServer.Core;

namespace AlloyCms11.Models.Pages
{
    public interface IHasRelatedContent
    {
        ContentArea RelatedContentArea { get; }
    }
}
