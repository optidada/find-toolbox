#if NETFRAMEWORK
using System.Collections.Generic;
using EPiServer.Find;
using EPiServer.Find.Api.Querying;

namespace Optidada.SearchNavigation.Toolbox
{
    /// <summary>
    /// SetTimeout for Find 13 (CMS 11). Find 17 (CMS 12) has its own SetTimeout extension; a second one here would make
    /// every call ambiguous, so on CMS 12 Find's built-in method is used.
    /// </summary>
    public static class TimeoutSearchExtensions
    {
        /// <summary>Fails the request after <paramref name="milliseconds"/> instead of waiting for the default timeout.</summary>
        public static ITypeSearch<TResult> SetTimeout<TResult>(this ITypeSearch<TResult> search, int milliseconds)
            => new Search<TResult, IQuery>(search, context => AddTimeout(context, milliseconds));

        /// <summary>Fails the request after <paramref name="milliseconds"/> instead of waiting for the default timeout.</summary>
        public static ISearch<TResult> SetTimeout<TResult>(this ISearch<TResult> search, int milliseconds)
            => new Search<TResult, IQuery>(search, context => AddTimeout(context, milliseconds));

        /// <summary>Sets the timeout on every search of the multi search.</summary>
        public static IMultiSearch<TResult> SetTimeout<TResult>(this IMultiSearch<TResult> multiSearch, int milliseconds)
        {
            var searches = new List<ISearch<TResult>>(multiSearch.Searches);
            multiSearch.Searches.Clear();
            foreach (var search in searches)
            {
                multiSearch.Searches.Add(search.SetTimeout(milliseconds));
            }

            return multiSearch;
        }

        private static void AddTimeout(ISearchContext context, int milliseconds)
        {
            var existingAction = context.CommandAction;
            context.CommandAction = command =>
            {
                existingAction?.Invoke(command);
                command.ExplicitRequestTimeout = milliseconds;
            };
        }
    }
}
#endif
