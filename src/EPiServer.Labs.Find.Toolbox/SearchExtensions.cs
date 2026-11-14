using EPiServer.Find;
using EPiServer.Find.Cms;
using EPiServer.Find.Api.Querying;
using EPiServer.Find.Helpers;
using EPiServer.Find.UnifiedSearch;
using System.Collections.Generic;
using System.Linq;
using EPiServer.Find.Framework;
using System;

namespace EPiServer.Labs.Find.Toolbox
{

#if NETFRAMEWORK
    /// <summary>
    /// SetTimeout for Find 13 (CMS 11). Find 17 (CMS 12) has its own SetTimeout extension; a second one here would make
    /// every call ambiguous, so on CMS 12 Find's built-in method is used.
    /// </summary>
    public static class SearchExtensions
    {
        // Note: the timeout is in MILLISECONDS (the parameter used to be misnamed durationInSeconds).

        public static ITypeSearch<TResult> SetTimeout<TResult>(this ITypeSearch<TResult> search, int milliseconds)
        {
            return new Search<TResult, IQuery>(search, context =>
            {
                var existingAction = context.CommandAction;
                context.CommandAction = command =>
                {
                    if (existingAction.IsNotNull())
                    {
                        existingAction(command);
                    }
                    command.ExplicitRequestTimeout = milliseconds;
                };

            });
        }

        public static ISearch<TResult> SetTimeout<TResult>(this ISearch<TResult> search, int milliseconds)
        {
            return new Search<TResult, IQuery>(search, context =>
            {
                var existingAction = context.CommandAction;
                context.CommandAction = command =>
                {
                    if (existingAction.IsNotNull())
                    {
                        existingAction(command);
                    }
                    command.ExplicitRequestTimeout = milliseconds;
                };

            });
        }

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

    }
#endif
}
