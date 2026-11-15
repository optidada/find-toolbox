# Changelog

## 3.0.0-beta.3

**New package name: `Optidada.SearchNavigation.Toolbox`** (formerly `EPiServer.Labs.Find.Toolbox`). See the
[upgrade guide](README.md#upgrading-from-1x--2x) for the renamed types.

- One package for CMS 11 (`net48`, Find 13.6.1) and CMS 12 (`net8.0`, Find 17.1+).
- Fix: the Find service rejects query string queries over 150 characters that contain `^` or `~` (400, the whole
  search fails); synonym expansion could reach that from two words. New `MaxBoostedQueryLength` (150) drops synonyms
  (never the user's words) and fuzzy words until the query fits.
- All extensions work on one shared main query: they can be combined in any order after `For()`;
  `InFieldImproved()` works after `UsingRelevanceImproved()` / `FuzzyMatch()` and writes boosts with invariant culture.
- `services.AddSearchNavigationToolbox(...)` for CMS 12; new `PhraseSlop` option.
- Code organised by feature (`Queries`, `Synonyms`, `SearchProviders`), one public type per file, internals internal,
  nullable reference types, XML documentation in the package.
- CI (build, unit tests, pack) and GitHub releases from `v*` tags; performance benchmark project.

## 3.0.0-beta.2

- Stemmed synonym lookup (Snowball): `notebooks` finds the synonyms of `notebook`, `koppar` those of `kopp`.

## 3.0.0-beta.1

- `MinimumShouldMatch()` works with synonyms: a term and its synonyms count as one term; a single term or synonym
  group sends no minimum_should_match.
- `WithAndAsDefaultOperator()` is kept with synonyms; multi word and quoted synonyms work.
- Synonyms rank below the user's own words (`SynonymBoost`, `SynonymVariationBoostFactor`); phrase and title boosts.
- `FuzzyMatch()` / `WildcardMatch()` also search the language field (stems, compound parts).
- Upper case AND / OR / NOT are ignored by default (`BooleanOperators`); the raw query (search statistics) is never
  changed.
- Configurable limits and boosts (`ToolboxOptions`); synonym loading with cache and retries.
- CMS edit UI search providers for pages, media and blocks fixed for CMS 11 and CMS 12.
- Alloy test sites for CMS 11 and CMS 12, 250+ integration tests against a live index, product search demo with facets.

## 1.x - 2.x

Released as `EPiServer.Labs.Find.Toolbox` from https://github.com/episerver/EPiServer.Labs.Find.Toolbox.
