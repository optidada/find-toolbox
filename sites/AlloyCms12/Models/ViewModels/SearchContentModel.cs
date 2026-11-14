using AlloyCms12.Models.Pages;

namespace AlloyCms12.Models.ViewModels;

public class SearchContentModel : PageViewModel<SearchPage>
{
    public SearchContentModel(SearchPage currentPage)
        : base(currentPage)
    {
    }

    public bool SearchServiceDisabled { get; set; }

    public string SearchedQuery { get; set; }

    public int NumberOfHits { get; set; }

    public IEnumerable<SearchHit> Hits { get; set; }

    /// <summary>The Find Toolbox chain that produced the results (shown below the results).</summary>
    public string ToolboxChain { get; set; }

    /// <summary>The query sent to Find.</summary>
    public string QueryJson { get; set; }

    /// <summary>Links that re-run the search with an extension switched off.</summary>
    public IDictionary<string, string> Switches { get; set; }

    public class SearchHit
    {
        public string Title { get; set; }

        public string Url { get; set; }

        public string Excerpt { get; set; }

        public double Score { get; set; }
    }
}
