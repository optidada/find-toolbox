# EPiServer.Labs.Find.Toolbox

Better search for **Optimizely Search & Navigation (Find)**: synonyms that respect `MinimumShouldMatch` and AND,
relevance boosting for phrases and title matches, typo and partial-word matching, and improved CMS edit-UI search.

One package, two builds:

| Target | CMS | Search & Navigation (Find) |
|---|---|---|
| `net48` | CMS 11 | 13.6.1 (latest for CMS 11) |
| `net8.0` | CMS 12 | 17.1 or later |

> Like other EPiServer.Labs projects this is not officially supported by Optimizely.

---

## Contents

- [Quick start](#quick-start)
- [What each extension does](#what-each-extension-does)
  - [MinimumShouldMatch()](#minimumshouldmatch)
  - [UsingSynonymsImproved()](#usingsynonymsimproved)
  - [UsingRelevanceImproved()](#usingrelevanceimproved)
  - [FuzzyMatch() and WildcardMatch()](#fuzzymatch-and-wildcardmatch)
  - [SetTimeout()](#settimeout)
- [Call order](#call-order)
- [Examples](#examples)
- [Configuration (limits and boosts)](#configuration-limits-and-boosts)
- [CMS edit UI search providers](#cms-edit-ui-search-providers)
- [Upgrading from 1.x / 2.x](#upgrading-from-1x--2x)
- [Repository layout and tests](#repository-layout-and-tests)

---

## Quick start

1. Install the NuGet package `EPiServer.Labs.Find.Toolbox` 3.x (one package for both CMS 11 and CMS 12; NuGet picks
   the right build). Packages are attached to the [GitHub releases](https://github.com/optidada/find-toolbox/releases).
2. Add the namespace:

   ```csharp
   using EPiServer.Labs.Find.Toolbox;
   ```

3. Replace any use of Find's `.UsingSynonyms()` with `.UsingSynonymsImproved()`.
4. Use the recommended chain:

   ```csharp
   var results = SearchClient.Instance.UnifiedSearch(Language.English)
       .For(query)
       .MinimumShouldMatch("2<-25%")        // or .WithAndAsDefaultOperator()
       .UsingSynonymsImproved()
       .UsingRelevanceImproved(x => x.SearchTitle)
       .FuzzyMatch(x => x.SearchTitle)
       .WildcardMatch(x => x.SearchTitle)
       .SetTimeout(10000)                   // milliseconds
       .GetResult();                        // CMS 12: await ... .GetResultAsync()
   ```

That's it: synonyms from the Find UI (Search & Navigation → Synonyms) are applied, every synonym counts as the term it
replaces for `MinimumShouldMatch`, pages containing the user's own words rank above pages that only match a synonym,
and exact phrases and titles starting with the query rank first.

---

## What each extension does

All extensions build on the `query_string` query created by `.For()`.

### Why UsingSynonymsImproved() instead of Find's UsingSynonyms()

Find's built-in `.UsingSynonyms()` sends the query to a server-side synonym analyzer (`synonym_<language>`) that uses
Find's synonym index. `.UsingSynonymsImproved()` loads the same synonym list (the one editors manage in the Find UI)
and expands the query on the client, inside the `query_string` that `MinimumShouldMatch()`, AND and the rest of the
toolbox work with.

Measured on the integration test data with **UnifiedSearch** (synonyms `find => search & navigation`,
`new york <=> nyc`, `Colour => Color`, `car => automobile`; identical on CMS 11 + Find 13.6.1 and CMS 12 + Find 17.1):

| Query | Expected | `UsingSynonyms()` | `UsingSynonymsImproved()` |
|---|---|---|---|
| `car insurance` (AND) | Automobile insurance basics | ✅ | ✅ |
| `find` (OR) | Episerver Find is fast, Search & Navigation from Episerver, Find your way home | ✅ | ✅ |
| `nyc pizza` (AND) | Pizza in New York, Best NYC pizza | ✅ | ✅ |
| `colour palette` (AND) | Color palette guide | ✅ | ✅ |
| `episerver find` (`WithAndAsDefaultOperator()`) | Episerver Find is fast, Search & Navigation from Episerver | ❌ also "Find your way home" and "Episerver CMS editing" (one word is enough) | ✅ |
| `episerver find` (`MinimumShouldMatch("100%")`) | the same two | ❌ the same four | ✅ |
| `episerver find editing fast` (`MinimumShouldMatch("75%")`) | Episerver Find is fast | ❌ four pages | ✅ |
| `new york pizza` (AND) | Pizza in New York, Best NYC pizza | ❌ only "Pizza in New York" | ✅ |
| `"new york" pizza` (AND) | Pizza in New York, Best NYC pizza | ❌ none | ✅ |

In short: simple synonyms work with both. With **AND** or **MinimumShouldMatch** the built-in returns documents that
match only one of the words, and **multi-word phrases** (`new york`) and **quoted phrases** are not expanded. The
improved synonyms also rank synonym matches below the user's own words and have configurable expansion limits.

> Note: the built-in only applies synonyms when the query uses the language analyzer, i.e. with `UnifiedSearch` or
> language specific fields. A typed `Search<T>().For(q).UsingSynonyms()` without language fields uses the generic
> `synonym` analyzer and expanded nothing in the tests. `UsingSynonymsImproved()` works for both.

Compare on the test sites: `/toolbox-test/search?q=episerver find&mode=and&unified=true&builtinSynonyms=true`.

### Does it actually search better? Measured

The product demo (`/product-demo`, 40 products, synonyms `laptop <=> notebook`, `tv <=> television`,
`mobile phone <=> smartphone`, `earbuds => headphones`) was searched with 19 realistic queries, each with a judged set
of relevant products, using three engines on the same fields: plain Find with its built-in `UsingSynonyms()` and OR
or AND, and the Find Toolbox chain from [Quick start](#quick-start). Same results on CMS 11 + Find 13.6.1 and
CMS 12 + Find 17.1.

| | Find, OR | Find, AND | **Find Toolbox** |
|---|---|---|---|
| Recall (share of relevant products found) | 84% | 76% | **95%** |
| Precision (share of returned products that are relevant) | 42% | 64% | **95%** |
| Mean reciprocal rank (is the first hit relevant?) | 0.74 | 0.72 | **1.00** |
| Queries with no hits | 3 | 4 | **0** |
| Queries answered perfectly (all relevant, nothing else) | 5 / 19 | 10 / 19 | **15 / 19** |

Where the difference comes from:

| Query | Find, OR | Find, AND | Find Toolbox |
|---|---|---|---|
| `fabrikam laptop` | 16 hits (all Fabrikam + all laptops) | 16 hits | **the 2 Fabrikam laptops** |
| `gaming laptop` | 9 hits, the gaming laptop 2nd | 9 hits | **1 hit** |
| `northwind tv` | 16 hits, first relevant 4th | 16 hits | 10 hits, **first hit relevant** |
| `mobile phone` | 8 (incl. the flip phone) | 3 of 7 | **7 of 7** |
| `fabrikam camra` (typo) | 10 hits, first relevant 4th | 0 | **the 2 Fabrikam cameras** |
| `headphnes` (typo) | 0 | 0 | **5 of 7** |
| `televisoin` (typo) | 0 | 0 | **3 of 8** |
| `cam` (partial word) | 0 | 0 | **8 of 8 cameras** |

Simple one word synonyms work in all three. The gains are `MinimumShouldMatch` working together with synonyms
(OR finds too much, AND too little), typos and partial words, and ranking the best match first.

Remaining misses: a typo in a word that has synonyms only finds the documents with the corrected word itself
(`televisoin` finds the "TV"s but not the "Television"s), fuzzy and wildcard do not combine with synonyms; and words
written apart (`note book`) do not find compounds (`notebook`) unless the language analyzer decompounds.

Reproduce: start the test sites and run `python tests/evaluation/evaluate_product_search.py`, or compare single
queries with `/toolbox-test/products?q=...&engine=find|find-and|toolbox`.

### MinimumShouldMatch()

How many of the query's terms a document must contain. A middle ground between OR (too many hits) and AND (no hits).

| Value | Meaning |
|---|---|
| `"2"` | At least 2 terms (all terms if the query has fewer). |
| `"100%"` | All terms (same as AND). |
| `"75%"` | 75% of the terms, rounded **down**: 3 of 4, 2 of 3, 1 of 2. |
| `"-25%"` | All but 25% may be missing (rounded down): 3 of 4, 3 of 3, 2 of 2. |
| `"2<75%"` | Up to 2 terms: all required. More than 2: 75%. |
| `"2<-25%"` | Up to 2 terms: all required. More than 2: all but 25%. A good default. |

> **Percentages round down.** `"2<60%"` on a 3 term query requires only **1** term (60% of 3 = 1.8 → 1).
> Use negative percentages (`"2<-40%"`) or several conditions (`"2<-1 5<75%"`) if that is not what you want.

`MinimumShouldMatch()` overrides `WithAndAsDefaultOperator()`. It requires a Find "v3" index; ask Optimizely support if unsure.

### UsingSynonymsImproved()

Expands the query with the synonyms editors manage in the Find UI, on the client side.

```
Query:     episerver find              Synonym: find => search & navigation
Becomes:   episerver (find OR (search AND navigation)^0.05)
```

What that gives you:

- **A synonym group counts as one term.** With `MinimumShouldMatch("100%")` the query above requires
  `episerver` **and** (`find` **or** `search navigation`). Upstream 2.x sent the synonyms as a separate OR'ed
  clause, which let documents through that only matched the synonym.
- **AND stays AND.** `.WithAndAsDefaultOperator()` is kept (1.x turned it into OR when used with synonyms).
- **Multi-word synonyms require all their words** (`search AND navigation`), in any order.
- **Bidirectional** synonyms and **multi-word phrases** work (`new york <=> nyc`), longest phrase first, no
  overlapping expansions, case-insensitive (`Colour` in the Find UI matches `colour`).
- **Synonyms rank below the user's own words.** Synonym alternatives get a low boost (`SynonymBoost`, normalized by
  length so a longer synonym does not score higher just by having more words).
- **No dependency on the synonym index** being up to date; no unwanted built-in synonyms.

Synonyms are cached per language (default 1 hour). While testing, pass a short duration:

```csharp
.UsingSynonymsImproved(TimeSpan.FromSeconds(1))
```

If loading synonyms from Find fails, the (partial) list is only cached for 1 minute (`SynonymFailureCacheDuration`).

### UsingRelevanceImproved()

Adds optional clauses on the fields you pass, for the user's query and up to 2 synonym variations. They mostly
re-rank: the phrase clauses only match documents containing all the words. The prefix clause also finds fields that
start with a partial word (`epis` finds "Episerver ...").

| Clause | Boost | Ranks higher when |
|---|---|---|
| `match_phrase` | 10 | the field contains the phrase (or, for one word queries, the word) |
| `match_phrase_prefix` (lowercase field) | 5 | the field **starts with** the phrase |
| `prefix` (one word queries, 3+ chars) | 0.5 | the field starts with the word |

Synonym variations get `SynonymVariationBoostFactor` (0.2) of these boosts, normalized by length.

```csharp
.UsingRelevanceImproved(x => x.SearchTitle)                 // UnifiedSearch
.UsingRelevanceImproved(x => x.Name, x => x.MetaTitle)      // typed search
```

Use short fields (titles, names). Phrase prefix on long text fields is costly and rarely useful.

### FuzzyMatch() and WildcardMatch()

- `FuzzyMatch()` finds words with typos (`episervr` → `episerver`, `ait` → `air`). Edit distance is
  Elasticsearch's AUTO (1 edit for 3-5 characters, 2 above); the first character must match (`FuzzyPrefixLength`,
  default 1; upstream used 3, which never corrected a typo in the first three letters).
- `WildcardMatch()` finds partial words (`episerv` → `episerver`), right side only.
- When the search has a language (`Search<T>(Language.Swedish)`, `UnifiedSearch(Language.English)`), both search the
  language field (`.sv`, `.en`) as well as the language neutral one, so they get the language analyzer's stemming and
  index-time decompounding (e.g. Swedish compounds), like `For()`/`InField()` do.
- Both use the user's words (not synonyms), only words of 3-16 characters and at most the first 3 of them, inherit
  `MinimumShouldMatch` and get a low boost (0.4 and 0.2) so exact matches rank first.
- `WildcardMatch(boost, doubleSided: true, ...)` adds a leading wildcard (`*term*`). **Very expensive** (it scans
  every term of the field); only use it on small fields.

Both are heavier than normal queries: use them on few, short fields.

### SetTimeout()

Sets the request timeout, in **milliseconds**:

```csharp
.SetTimeout(10000) // 10 seconds
```

- **CMS 11 / Find 13:** provided by the toolbox (`ITypeSearch`, `ISearch`; an `IMultiSearch` overload exists but is
  not covered by the tests yet).
- **CMS 12 / Find 17:** Find has its own `SetTimeout`, so the toolbox does not add one (two would make every call
  ambiguous). The same code compiles against both.

### Query syntax

Find's `.For()` escapes almost all query syntax, so `-`, `+`, `*`, `~`, `:`, parentheses etc. are literal characters
(`e-mail`, `c++`, `covid-19` and `cats -dogs` are just words). Real syntax is limited to:

- **Quoted phrases** (`"new york" pizza`): kept, and phrases can have synonyms too.
- **Upper case `AND`, `OR`, `NOT`** (outside quotes): the toolbox leaves such queries exactly as Find interprets
  them, without synonyms, relevance, fuzzy or wildcard clauses. Rewriting would drop the operators, and the extra
  optional clauses could match documents that `NOT` excludes.

---

## Call order

The extensions rewrite the query created by `.For()`, so the order matters:

```
.For(query)                         1. always first: creates the query_string query
.InField(...)                       2. fields
.WithAndAsDefaultOperator()         3. operator (or MinimumShouldMatch)
.MinimumShouldMatch("...")          4. before or after UsingSynonymsImproved, both work
.UsingSynonymsImproved()            5.
.UsingRelevanceImproved(...)        6. after synonyms: boosts the synonym variations too
.FuzzyMatch(...) / .WildcardMatch() 7.
.Filter(...) / .Take(...) / ...     8. anything else
```

- Without `.For()` the extensions do nothing (a trace message is logged).
- Do not call Find's `.InField()` after `UsingRelevanceImproved()`, `FuzzyMatch()` or `WildcardMatch()`: those wrap
  the query in a bool query.

---

## Examples

**Site search with UnifiedSearch (CMS 12, async)**

```csharp
public async Task<UnifiedSearchResults> SearchAsync(string query, int page)
{
    return await _client.UnifiedSearch(Language.English)
        .For(query)
        .MinimumShouldMatch("2<-25%")
        .UsingSynonymsImproved()
        .UsingRelevanceImproved(x => x.SearchTitle)
        .FuzzyMatch(x => x.SearchTitle)
        .WildcardMatch(x => x.SearchTitle)
        .Skip((page - 1) * 10)
        .Take(10)
        .SetTimeout(10000)
        .GetResultAsync();
}
```

**Typed search on specific fields**

```csharp
var result = _client.Search<ArticlePage>()
    .For(query)
    .InField(x => x.Name, 5)
    .InField(x => x.MainIntro)
    .InField(x => x.SearchText())
    .MinimumShouldMatch("2<-25%")
    .UsingSynonymsImproved()
    .UsingRelevanceImproved(x => x.Name)
    .Filter(x => x.Ancestors().Match(startPage.ID.ToString()))
    .Take(20)
    .GetContentResult();
```

**All words required (AND), with synonyms**

```csharp
.For(query)
.WithAndAsDefaultOperator()
.UsingSynonymsImproved()
```

**Any word (OR, the Find default), with synonyms and relevance**

```csharp
.For(query)
.UsingSynonymsImproved()
.UsingRelevanceImproved(x => x.SearchTitle)
```

**Testing synonyms you just added in the Find UI**

```csharp
.UsingSynonymsImproved(TimeSpan.FromSeconds(1))
```

**What MinimumShouldMatch + synonyms returns** (from the integration tests, synonym `find => search & navigation`):

| Query | Options | Returns |
|---|---|---|
| `episerver find` | `MinimumShouldMatch("100%")` | "Episerver Find is fast", "Search & Navigation from Episerver" |
| `episerver find` | `WithAndAsDefaultOperator()` | the same |
| `episerver find` | OR (default) | also "Find your way home", "Episerver CMS editing"; **not** "Search tips for gardeners" (the synonym needs both words) |
| `find` | `MinimumShouldMatch("100%")` | "Episerver Find is fast", "Search & Navigation from Episerver", "Find your way home" |
| `episerver find editing fast` | `MinimumShouldMatch("75%")` | "Episerver Find is fast" (3 of 4: episerver, find, fast) |

---

## Configuration (limits and boosts)

Everything is configurable through `FindToolboxOptions`. Defaults are safe for most sites.

**CMS 12** (`Startup.ConfigureServices`):

```csharp
services.Configure<FindToolboxOptions>(options =>
{
    options.MaxSynonymsPerPhrase = 5;
    options.SynonymCacheDuration = TimeSpan.FromMinutes(30);
});
```

**CMS 11** (an `IConfigurableModule`):

```csharp
[InitializableModule]
[ModuleDependency(typeof(EPiServer.Web.InitializationModule))]
public class FindToolboxConfiguration : IConfigurableModule
{
    public void ConfigureContainer(ServiceConfigurationContext context)
    {
        context.Services.Configure<FindToolboxOptions>(options => options.MaxSynonymsPerPhrase = 5);
    }

    public void Initialize(InitializationEngine context) { }
    public void Uninitialize(InitializationEngine context) { }
}
```

| Option | Default | |
|---|---|---|
| **Limits** | | Keep queries from expanding too much |
| `MaxQueryTerms` | 50 | Terms taken from the user query |
| `MaxSynonymPhraseTerms` | 10 | Longest synonym phrase looked up (bounded by the longest phrase in your list) |
| `MaxSynonymsPerPhrase` | 10 | Synonyms added for one matched phrase |
| `MaxExpandedPhrases` | 5 | Phrases expanded per query; later matches stay unexpanded |
| `MaxQueriesForMatch` | 3 | Query variations (original + synonyms) used by `UsingRelevanceImproved()` |
| `MaxRelevanceTerms` | 10 | Words in the relevance phrase queries |
| `MinPrefixLength` | 3 | Min length for the single word prefix boost |
| `MinFuzzyWildcardTermLength` / `Max...` | 3 / 16 | Word length for fuzzy/wildcard |
| `MaxFuzzyWildcardTerms` | 3 | Words that get fuzzy/wildcard |
| `FuzzyPrefixLength` | 1 | Leading characters that must match exactly in fuzzy matching |
| **Boosts** | | Tuned against a live index, see the relevance tests |
| `PhraseBoost` | 10 | Field contains the phrase/word |
| `PhrasePrefixBoost` | 5 | Field starts with the phrase |
| `PrefixBoost` | 0.5 | Field starts with the word |
| `FuzzyBoost` / `WildcardBoost` | 0.4 / 0.2 | Default boosts of `FuzzyMatch()` / `WildcardMatch()` |
| `SynonymBoost` | 0.1 | Synonym alternatives in the query (normalized by length). 1 = same weight as the user's word |
| `SynonymVariationBoostFactor` | 0.2 | Factor on the relevance boosts for synonym variations |
| **Synonym loading** | | |
| `SynonymCacheDuration` | 1 hour | |
| `SynonymFailureCacheDuration` | 1 minute | Cache time when loading failed |
| `SynonymLoadAttempts` / `SynonymRetryDelay` | 3 / 200 ms | Retries (with backoff) per batch |
| **Edit UI search providers** | | |
| `SearchProviderMinimumShouldMatch` | `"1"` | |
| `SearchProviderMaxResults` | 20 | |
| `SearchProviderCacheDuration` | off | Results cache; off so editors find new content immediately |

Use your own synonym source by registering an `ISynonymProvider` (the default is `SynonymLoader`, which reads the
synonyms from Find).

---

## CMS edit UI search providers

Three search providers bring all of the above to the CMS edit UI: **Find Toolbox - Pages**, **Find Toolbox - Media**
and **Find Toolbox - Blocks**.

1. Open the search provider settings in CMS admin (CMS 11: Admin → Config → Search providers).
2. Enable the three Find Toolbox providers and drag them to the top.
3. Disable Find's pages, blocks and files providers.

They search the name (boost 10), all text (1) and the content type name (0.5); a numeric query also matches the
content ID, and the page provider matches absolute URLs and publish dates.

---

## Upgrading from 1.x / 2.x

- **Namespace:** everything is in `EPiServer.Labs.Find.Toolbox` (1.x for CMS 11 used `EPiServer.Find.Cms`).
- **Find versions:** CMS 12 requires Find 17.1+; CMS 11 is built against Find 13.6.1.
- **Ranking changes:** synonyms now rank below the user's own words, single word queries get a title boost, and
  `MinimumShouldMatch` is respected with synonyms. Set `SynonymBoost = 1` and `SynonymVariationBoostFactor = 1`
  to weigh synonyms like 2.x did.
- `MinimumShouldMatch()` returns `IQueriedSearch<T, QueryStringQuery>` (was `...MinShouldMatchQueryStringQuery`).
- `SetTimeout(int milliseconds)`: only the parameter name changed (it was called `durationInSeconds` but always was
  milliseconds).
- Replace Find's `.UsingSynonyms()` with `.UsingSynonymsImproved()` (see [the comparison](#why-usingsynonymsimproved-instead-of-finds-usingsynonyms)).
- `UsingSynonymService` and `SynonymLoader` take `FindToolboxOptions` (and `UsingSynonymService` an
  `ISynonymProvider`) in their constructors; only relevant if you construct them yourself.
- Prefix boosting now starts at 3 characters (2.x: 2) and relevance phrases use at most 10 words.

---

## Repository layout and tests

```
src/EPiServer.Labs.Find.Toolbox/                   the library (net48 + net8.0)
tests/EPiServer.Labs.Find.Toolbox.UnitTests/       generated query JSON, limits, boosts; no Find index needed
tests/EPiServer.Labs.Find.Toolbox.IntegrationTests/ real searches against the Alloy test sites
sites/AlloyCms11/                                  Alloy MVC, CMS 11, Find 13.6.1 (net48, IIS Express)
sites/AlloyCms12/                                  Alloy MVC, CMS 12, Find 17.1 (net8.0)
sites/Shared/ToolboxTestHarness/                   test data set and /toolbox-test endpoints (shared by the sites)
```

**Unit tests** (run on both frameworks):

```
dotnet test tests/EPiServer.Labs.Find.Toolbox.UnitTests
```

**Integration tests** need a Find index (a dedicated test index: the tests write pages and synonyms to it).

```bash
# 1. Index settings for the CMS 12 site (stored outside the repo)
cd sites/AlloyCms12
dotnet user-secrets set "EPiServer:Find:ServiceUrl" "https://..."
dotnet user-secrets set "EPiServer:Find:DefaultIndex" "my_test_index"

# 2. Start the site (creates its LocalDB database and imports the Alloy content on first start)
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5112

# 3. Run the tests (they seed the test pages and synonyms first)
TOOLBOX_CMS12_URL=http://localhost:5112 dotnet test tests/EPiServer.Labs.Find.Toolbox.IntegrationTests

# Optional: wipe ALL documents and synonyms in the index before seeding
TOOLBOX_RESET_INDEX=true TOOLBOX_CMS12_URL=http://localhost:5112 dotnet test tests/EPiServer.Labs.Find.Toolbox.IntegrationTests
```

Tests for a site whose `TOOLBOX_<SITE>_URL` is not set are skipped. Both sites can share one test index.

**CMS 11 site** (classic ASP.NET: build with Visual Studio or MSBuild, run with IIS Express):

```bash
cd sites/AlloyCms11
# Index settings: copy episerver.find.config.template to episerver.find.config (git-ignored) and fill it in
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe" AlloyCms11.csproj -restore
"C:/Program Files/IIS Express/iisexpress.exe" /path:%CD% /port:5111
TOOLBOX_CMS11_URL=http://localhost:5111 dotnet test ../../tests/EPiServer.Labs.Find.Toolbox.IntegrationTests
```

The build copies the CMS UI modules from the packages and seeds `App_Data/AlloyCms11.mdf` from the empty CMS
database; the CMS creates its schema and imports the Alloy content on first start. The site was created from the
Alloy MVC template of the Episerver CMS Visual Studio Extension 11.7 (`web.config` generated by applying the
packages' config transforms).

**Try it in the browser.** Both Alloy sites' search pages (`/en/search/`) use the recommended toolbox chain and show,
below the results, the extension chain used, hit scores, the query sent to Find, and links that switch single
extensions off (`?q=...&msm=100%25&synonyms=false&relevance=false&fuzzy=false&wildcard=false`).
On a fresh index, `POST /toolbox-test/index-site` indexes the Alloy demo content. On first start the sites ask you
to register an administrator.

The test sites expose development-only endpoints that are handy when tuning:

```
GET  /toolbox-test/search?q=episerver find&mode=msm&msm=100%&relevance=true&unified=true&set=relevance
     -> hits, scores and the exact query JSON sent to Find
     options: mode=or|and|msm, msm, synonyms, msmAfterSynonyms, relevance, fuzzy, wildcard, inFields,
              unified, set=scenarios|relevance, and boost overrides (phraseBoost, synonymBoost, ...)
GET  /toolbox-test/provider?area=pages|media|blocks&q=...
POST /toolbox-test/seed[?resetIndex=true]
POST /toolbox-test/index-site
GET  /toolbox-test/count
```

[![License](http://img.shields.io/:license-apache-blue.svg?style=flat-square)](http://www.apache.org/licenses/LICENSE-2.0.html)
