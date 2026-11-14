using EPiServer.Find;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Cms;
using EPiServer.Find.Helpers;
using EPiServer.Find.Helpers.Text;
using EPiServer.Find.Tracing;
using EPiServer.Logging;
using EPiServer.ServiceLocation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EPiServer.Labs.Find.Toolbox
{
    public static class QueryStringQueryExtensions
    {
        private static Lazy<UsingSynonymService> _lazyUsingSynonymService = new Lazy<UsingSynonymService>(() => ServiceLocator.Current.GetInstance<UsingSynonymService>());
        private static readonly ILogger log = LogManager.GetLogger(typeof(SearchRequestExtensions));


        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search, TimeSpan? cacheDuration = null)
        {
            return search.UsingSynonymsImproved(_lazyUsingSynonymService.Value, cacheDuration);
        }

        public static IQueriedSearch<TSource, QueryStringQuery> UsingSynonymsImproved<TSource>(this IQueriedSearch<TSource> search, UsingSynonymService usingSynonymService, TimeSpan? cacheDuration = null)
        {
            return usingSynonymService.UsingSynonyms(search, cacheDuration);
        }

        public static IQueriedSearch<TSource, QueryStringQuery> MinimumShouldMatch<TSource>(this IQueriedSearch<TSource> search, string minMatch)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                if (!QueryHelpers.GetFirstQueryStringQuery(context, out IQuery currentQuery, out BoolQuery currentBoolQuery))
                {
                    Trace.Instance.Add(new TraceEvent(search, "MinimumShouldMatch() relies on a QueryStringQuery, i.e. with the use of the .For() -extensions.") { IsError = false });
                    return;
                }

                var originalQuery = (QueryStringQuery)currentQuery;
                var query = new MinShouldMatchQueryStringQuery(originalQuery.Query);
                QueryHelpers.CopySettings(originalQuery, query);
                query.MinimumShouldMatch = minMatch;
                query.DefaultOperator = BooleanOperator.Or;
                // One term (or one synonym group): nothing to count, and the analyzer may turn the single word into
                // several tokens (e.g. word + stem) that minimum_should_match would then all require.
                query.IsSingleClause = query.IsSingleClause || IsSingleTerm(QueryHelpers.GetRawQueryString(originalQuery));

                if (ReferenceEquals(context.RequestBody.Query, currentBoolQuery) && currentBoolQuery.Should.Count > 0)
                {
                    currentBoolQuery.Should[0] = query;
                    return;
                }

                context.RequestBody.Query = query;
                log.Debug("Added MinimumShouldMatch to search");
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> UsingRelevanceImproved<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery newBoolQuery = new BoolQuery();
                BoolQuery currentBoolQuery;
                IQuery currentQuery;

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentQuery, out currentBoolQuery))
                {
                    Trace.Instance.Add(new TraceEvent(search, "The use of UsingRelevanceImproved() relies on a QueryStringQuery, i.e. with the use of the .For() -extensions.") { IsError = false });
                    return;
                }

                // AND / OR / NOT: optional clauses could match documents the operators exclude
                if (QueryHelpers.HasBooleanOperators(QueryHelpers.GetRawQueryString(currentQuery)))
                {
                    return;
                }

                // Keep the current bool query with current queries if it exists
                if (currentBoolQuery.Should.Count > 0)
                {
                    newBoolQuery = currentBoolQuery;
                }
                else // Or use the QueryStringQuery
                {
                    newBoolQuery.Should.Add(currentQuery);
                }


                IList<string> queriesForMatch = null;

                // If .UsingSynonymsImproved() was used we use QueriesForMatch (original + synonym variations) instead of the raw query
                if (currentQuery is MinShouldMatchQueryStringQuery msmQuery && msmQuery.QueriesForMatch != null && msmQuery.QueriesForMatch.Length > 0)
                {
                    queriesForMatch = msmQuery.QueriesForMatch;
                }
                else
                {
                    queriesForMatch = new[] { string.Join(" ", QueryHelpers.GetQueryPhrases(QueryHelpers.GetRawQueryString(currentQuery))) };
                }


                var options = FindToolboxOptions.Current;
                var originalTermCount = 1;
                for (var variation = 0; variation < queriesForMatch.Count; variation++)
                {
                    // match_phrase / match_phrase_prefix / prefix are not query_string queries: no escaping, no quotes.
                    // Punctuation only tokens (e.g. & in "search & navigation") are dropped: Find's shingled fields index them
                    // as tokens, which inflates phrase scores. Limit the number of terms: match_phrase_prefix expands the last term.
                    var terms = queriesForMatch[variation].Replace("\"", string.Empty)
                                             .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Where(t => t.Any(char.IsLetterOrDigit))
                                             .Take(Math.Max(1, options.MaxRelevanceTerms))
                                             .ToArray();
                    if (terms.Length == 0)
                    {
                        continue;
                    }

                    // The first variation is the user's query; the rest are synonym variations. Synonym variations get
                    // a lower boost, normalized by length: phrase scores grow with the number of (shingled) terms, so a
                    // longer synonym would otherwise outrank the user's own words.
                    double factor;
                    if (variation == 0)
                    {
                        originalTermCount = terms.Length;
                        factor = 1;
                    }
                    else
                    {
                        factor = options.SynonymVariationBoostFactor * Math.Min(1.0, (double)originalTermCount / terms.Length);
                    }

                    var query = string.Join(" ", terms);

                    foreach (var fieldSelector in fieldSelectors)
                    {
                        var fieldNameLowercase = search.Client.Conventions.FieldNameConvention.GetFieldNameForLowercase(fieldSelector);
                        var fieldNameAnalyzed = search.Client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector);

                        if (terms.Length == 1)
                        {
                            // Field contains the term
                            if (terms[0].Length > 1)
                            {
                                newBoolQuery.Should.Add(new PhraseQuery(fieldNameAnalyzed, query) { Boost = options.PhraseBoost * factor });
                            }

                            // Field starts with the term
                            if (terms[0].Length >= options.MinPrefixLength)
                            {
                                newBoolQuery.Should.Add(new PrefixQuery(fieldNameLowercase, query.ToLowerInvariant()) { Boost = options.PrefixBoost * factor });
                            }
                        }
                        else if (terms.Any(x => x.Length > 1))
                        {
                            // Field contains the phrase / field starts with the phrase
                            newBoolQuery.Should.Add(new PhraseQuery(fieldNameAnalyzed, query) { Boost = options.PhraseBoost * factor });
                            newBoolQuery.Should.Add(new PhrasePrefixQuery(fieldNameLowercase, query.ToLowerInvariant()) { Boost = options.PhrasePrefixBoost * factor });
                        }
                    }
                }

                log.Debug("Added PrefixQuery, PhraseQuery and PhrasePrefixQuery to search");
                context.RequestBody.Query = newBoolQuery;
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return search.FuzzyMatch(FindToolboxOptions.Current.FuzzyBoost, fieldSelectors);
        }

        public static IQueriedSearch<TSource, QueryStringQuery> FuzzyMatch<TSource>(this IQueriedSearch<TSource> search, double boost, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery newBoolQuery = new BoolQuery();
                BoolQuery currentBoolQuery;
                IQuery currentQuery;

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentQuery, out currentBoolQuery))
                {
                    Trace.Instance.Add(new TraceEvent(search, "The use of FuzzyMatch relies on QueryStringQuery, i.e. with the use of the .For() -extensions.") { IsError = false });
                    return;
                }

                // AND / OR / NOT: optional clauses could match documents the operators exclude
                if (QueryHelpers.HasBooleanOperators(QueryHelpers.GetRawQueryString(currentQuery)))
                {
                    return;
                }

                // Keep the current bool query with current queries if it exists
                if (currentBoolQuery.Should.Count > 0)
                {
                    newBoolQuery = currentBoolQuery;
                }
                else // Or use the QueryStringQuery
                {
                    newBoolQuery.Should.Add(currentQuery);
                }

                var query = QueryHelpers.GetRawQueryString(currentQuery);
                if (query.IsNullOrEmpty())
                {
                    return;
                }

                // Do not trigger on synonym expansions which are parenthesized and not on quoted searches.
                if (QueryHelpers.IsStringParenthesized(query) || QueryHelpers.IsStringQuoted(query))
                {
                    return;
                }

                var terms = QueryHelpers.GetQueryPhrases(query);

                // Only take terms > 2 chars and take max 3 of these
                var options = FindToolboxOptions.Current;
                string[] candidateTerms = FuzzyWildcardCandidates(terms, options)
                                               .Select(x => string.Format("{0}{1}", x.Quote(), "~")).ToArray();

                if (candidateTerms.Count() == 0)
                {
                    return;
                }

                List<string> fieldNames = new List<string>();
                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions
                                        .FieldNameConvention
                                        .GetFieldNameForAnalyzed(fieldSelector);
                    fieldNames.Add(fieldName);
                }

                var fuzzyQueryString = string.Join(" ", candidateTerms);
                var fuzzyQueryStringQuery = new MinShouldMatchQueryStringQuery(fuzzyQueryString)
                {
                    Fields = fieldNames,
                    DefaultOperator = (currentQuery as QueryStringQuery).DefaultOperator,
                    MinimumShouldMatch = (currentQuery as MinShouldMatchQueryStringQuery)?.MinimumShouldMatch,
                    FuzzyPrefixLength = options.FuzzyPrefixLength,
                    Boost = boost
                };

                newBoolQuery.Should.Add(fuzzyQueryStringQuery);
                context.RequestBody.Query = newBoolQuery;
                log.Debug("Added fuzzyMatch {0} to search", fuzzyQueryString);
            });
        }

        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return search.WildcardMatch(FindToolboxOptions.Current.WildcardBoost, doubleSided: false, fieldSelectors);
        }

        public static IQueriedSearch<TSource, QueryStringQuery> WildcardMatch<TSource>(this IQueriedSearch<TSource> search, double boost, bool doubleSided, params Expression<Func<TSource, string>>[] fieldSelectors)
        {
            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                BoolQuery newBoolQuery = new BoolQuery();
                BoolQuery currentBoolQuery;
                IQuery currentQuery;

                if (!QueryHelpers.GetFirstQueryStringQuery(context, out currentQuery, out currentBoolQuery))
                {
                    // Synonyms are only supported for QueryStringQuery
                    Trace.Instance.Add(new TraceEvent(search, "The use of synonyms are only supported för QueryStringQueries, i.e. with the use of the .For() -extensions. The query will be executed without the use of synonyms.") { IsError = false });
                    return;
                }

                // AND / OR / NOT: optional clauses could match documents the operators exclude
                if (QueryHelpers.HasBooleanOperators(QueryHelpers.GetRawQueryString(currentQuery)))
                {
                    return;
                }

                // Keep the current bool query with current queries if it exists
                if (currentBoolQuery.Should.Count > 0)
                {
                    newBoolQuery = currentBoolQuery;
                }
                else // Or use the QueryStringQuery
                {
                    newBoolQuery.Should.Add(currentQuery);
                }

                var query = QueryHelpers.GetRawQueryString(currentQuery);
                if (query.IsNullOrEmpty())
                {
                    return;
                }

                // Do not trigger on synonym expansions which are parenthesized and not on quoted searches.
                if (QueryHelpers.IsStringParenthesized(query) || QueryHelpers.IsStringQuoted(query))
                {
                    return;
                }

                var terms = QueryHelpers.GetQueryPhrases(query);

                // Limit term size and term count
                string[] candidateTerms = FuzzyWildcardCandidates(terms, FindToolboxOptions.Current)
                                               .Select(x => doubleSided ? string.Format("*{0}*", x.Quote()) : string.Format("{0}*", x.Quote()))
                                               .ToArray();

                if (candidateTerms.Count() == 0)
                {
                    return;
                }

                List<string> fieldNames = new List<string>();
                foreach (var fieldSelector in fieldSelectors)
                {
                    string fieldName = search.Client.Conventions
                                        .FieldNameConvention
                                        .GetFieldNameForAnalyzed(fieldSelector);
                    fieldNames.Add(fieldName);
                }

                var wildcardQueryString = string.Join(" ", candidateTerms);
                var wildcardQuery = new MinShouldMatchQueryStringQuery(wildcardQueryString)
                {
                    Fields = fieldNames,
                    DefaultOperator = (currentQuery as QueryStringQuery).DefaultOperator,
                    MinimumShouldMatch = (currentQuery as MinShouldMatchQueryStringQuery)?.MinimumShouldMatch,
                    Boost = boost
                };

                newBoolQuery.Should.Add(wildcardQuery);
                context.RequestBody.Query = newBoolQuery;
                log.Debug("Added wildcard {0} to search", wildcardQueryString);
            });
        }

        private static bool IsSingleTerm(string rawQuery)
            => !string.IsNullOrWhiteSpace(rawQuery) && !QueryHelpers.HasBooleanOperators(rawQuery) && QueryHelpers.GetQueryPhrases(rawQuery).Length == 1;

        private static IEnumerable<string> FuzzyWildcardCandidates(IEnumerable<string> terms, FindToolboxOptions options)
        {
            return terms.Where(x => !QueryHelpers.IsStringQuoted(x)
                                    && x.Length >= options.MinFuzzyWildcardTermLength
                                    && x.Length <= options.MaxFuzzyWildcardTermLength)
                        .Take(Math.Max(0, options.MaxFuzzyWildcardTerms));
        }

        public static IQueriedSearch<TSource, QueryStringQuery> InFieldImproved<TSource, TExistingQuery>(
                this IQueriedSearch<TSource, TExistingQuery> search,
                Expression<Func<TSource, string>> fieldSelector,
                double? relativeImportance = null)
                where TExistingQuery : QueryStringQuery
        {
            fieldSelector.ValidateNotNullArgument("fieldSelector");

            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                var nonLanguageField = search.Client.Conventions.FieldNameConvention.GetFieldNameForAnalyzed(fieldSelector);
                var languageField = search.Client.Conventions.FieldNameConvention.GetFieldNameForSearch(fieldSelector, context.ContentLanguage);

                AddFieldToQueryStringQuery(context, nonLanguageField, relativeImportance);
                AddFieldToQueryStringQuery(context, languageField, relativeImportance);
            });

        }

        public static void AddFieldToQueryStringQuery(ISearchContext context, string fieldNameToAdd, double? relativeImportance)
        {
            var query = context.RequestBody.Query as MultiFieldQueryStringQuery;
            if (query.IsNull())
            {
                var originalQuery = (QueryStringQuery)context.RequestBody.Query;
                query = new MultiFieldQueryStringQuery(originalQuery.Query);
                QueryHelpers.CopySettings(originalQuery, query);
                if (originalQuery.DefaultField.IsNotNull())
                {
                    query.Fields.Add(originalQuery.DefaultField);
                }
            }

            if (relativeImportance != null)
            {
                var relativeImportanceString = relativeImportance.Value.ToString();
                fieldNameToAdd = string.Format("{0}^{1}", fieldNameToAdd, relativeImportanceString);
            }

            query.Fields.Add(fieldNameToAdd);
            context.RequestBody.Query = query;
        }

    }

}