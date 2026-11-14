using System.Globalization;
using AlloyCms12.Models.Pages;
using AlloyCms12.Models.ViewModels;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Framework;
using EPiServer.Find.Json;
using EPiServer.Find.UnifiedSearch;
using EPiServer.Labs.Find.Toolbox;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace AlloyCms12.Controllers;

/// <summary>
/// Alloy's search page, powered by the Find Toolbox (the chain recommended in the README).
/// Switches for trying things out: ?q=...&amp;msm=2&lt;-25%25&amp;synonyms=false&amp;relevance=false&amp;fuzzy=false&amp;wildcard=false
/// </summary>
public class SearchPageController : PageControllerBase<SearchPage>
{
    private readonly IClient _client;

    public SearchPageController(IClient client)
    {
        _client = client;
    }

    public async Task<ViewResult> Index(SearchPage currentPage, string q, string msm = "2<-25%",
        bool synonyms = true, bool relevance = true, bool fuzzy = true, bool wildcard = true)
    {
        var model = new SearchContentModel(currentPage)
        {
            SearchedQuery = q,
            SearchServiceDisabled = false,
        };

        if (string.IsNullOrWhiteSpace(q))
        {
            return View(model);
        }

        // Build the chain and record which toolbox extensions are used
        var chain = new List<string> { "client.UnifiedSearch(Language.English)", $"    .For(\"{q}\")" };
        IQueriedSearch<ISearchContent, QueryStringQuery> search = _client.UnifiedSearch(Language.English).For(q);

        if (!string.IsNullOrWhiteSpace(msm))
        {
            search = search.MinimumShouldMatch(msm);
            chain.Add($"    .MinimumShouldMatch(\"{msm}\")");
        }

        if (synonyms)
        {
            search = search.UsingSynonymsImproved();
            chain.Add("    .UsingSynonymsImproved()");
        }

        if (relevance)
        {
            search = search.UsingRelevanceImproved(x => x.SearchTitle);
            chain.Add("    .UsingRelevanceImproved(x => x.SearchTitle)");
        }

        if (fuzzy)
        {
            search = search.FuzzyMatch(x => x.SearchTitle);
            chain.Add("    .FuzzyMatch(x => x.SearchTitle)");
        }

        if (wildcard)
        {
            search = search.WildcardMatch(x => x.SearchTitle);
            chain.Add("    .WildcardMatch(x => x.SearchTitle)");
        }

        var final = search.Take(20).SetTimeout(10000);
        chain.Add("    .Take(20)");
        chain.Add("    .SetTimeout(10000)");
        chain.Add("    .GetResultAsync();");

        var result = await final.GetResultAsync();

        model.NumberOfHits = result.TotalMatching;
        model.Hits = result.Hits.Select(hit => new SearchContentModel.SearchHit
        {
            Title = hit.Document.Title,
            Url = hit.Document.Url,
            Excerpt = hit.Document.Excerpt,
            Score = hit.Score ?? 0,
        }).ToList();
        model.ToolboxChain = string.Join(Environment.NewLine, chain);
        model.QueryJson = QueryJson(final);
        model.Switches = new Dictionary<string, string>
        {
            ["without synonyms"] = Url(q, msm, false, relevance, fuzzy, wildcard),
            ["without relevance"] = Url(q, msm, synonyms, false, fuzzy, wildcard),
            ["without fuzzy/wildcard"] = Url(q, msm, synonyms, relevance, false, false),
            ["all words (100%)"] = Url(q, "100%", synonyms, relevance, fuzzy, wildcard),
            ["any word (OR)"] = Url(q, "", synonyms, relevance, fuzzy, wildcard),
            ["default"] = Url(q, "2<-25%", true, true, true, true),
        };

        return View(model);
    }

    private string Url(string q, string msm, bool synonyms, bool relevance, bool fuzzy, bool wildcard)
        => "?" + string.Join("&", new[]
        {
            "q=" + Uri.EscapeDataString(q),
            "msm=" + Uri.EscapeDataString(msm ?? string.Empty),
            "synonyms=" + synonyms.ToString().ToLowerInvariant(),
            "relevance=" + relevance.ToString().ToLowerInvariant(),
            "fuzzy=" + fuzzy.ToString().ToLowerInvariant(),
            "wildcard=" + wildcard.ToString().ToLowerInvariant(),
        });

    private static string QueryJson(ISearch search)
    {
        var context = new SearchContext();
        search.ApplyActions(context);
        var json = Serializer.Serialize(Serializer.CreateDefault(), context.RequestBody);
        return JObject.Parse(json)["query"]?.ToString(Newtonsoft.Json.Formatting.Indented);
    }
}
