using System;
using System.Globalization;
using System.Linq.Expressions;
using EPiServer.Find;
using EPiServer.Find.Api.Querying.Queries;
using EPiServer.Find.Helpers;
using Optidada.SearchNavigation.Toolbox.Queries;

namespace Optidada.SearchNavigation.Toolbox
{
    /// <summary><c>InFieldImproved()</c>: Find's InField() for the language neutral and the language field, in any order.</summary>
    public static class InFieldSearchExtensions
    {
        /// <summary>
        /// Adds a field to the query (like Find's InField) in both the language neutral and the language field. Works in any
        /// order with the other toolbox extensions.
        /// </summary>
        public static IQueriedSearch<TSource, QueryStringQuery> InFieldImproved<TSource, TExistingQuery>(
            this IQueriedSearch<TSource, TExistingQuery> search,
            Expression<Func<TSource, string>> fieldSelector,
            double? relativeImportance = null)
            where TExistingQuery : QueryStringQuery
        {
            fieldSelector.ValidateNotNullArgument(nameof(fieldSelector));

            return new Search<TSource, QueryStringQuery>(search, context =>
            {
                var conventions = search.Client.Conventions.FieldNameConvention;
                AddField(context, conventions.GetFieldNameForAnalyzed(fieldSelector), relativeImportance);
                AddField(context, conventions.GetFieldNameForSearch(fieldSelector, context.ContentLanguage), relativeImportance);
            });
        }

        /// <summary>Adds a field (with optional boost) to the main query, also when other toolbox extensions wrapped it.</summary>
        private static void AddField(ISearchContext context, string fieldName, double? relativeImportance)
        {
            var main = MainQuery.Find(null, context, "InFieldImproved()");
            if (main == null)
            {
                return;
            }

            if (!(main.Query is MultiFieldQueryStringQuery query))
            {
                var copy = ToolboxQueryStringQuery.CopyOf(main.Query, main.Query.Query?.ToString());
                if (main.Query.DefaultField.IsNotNull())
                {
                    copy.Fields.Add(main.Query.DefaultField);
                }

                main.Replace(copy);
                query = copy;
            }

            // Invariant culture: "0,5" (e.g. on a Swedish server) is not valid query syntax
            query.Fields.Add(relativeImportance == null
                ? fieldName
                : fieldName + "^" + relativeImportance.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
