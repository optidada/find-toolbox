using EPiServer.ServiceLocation;

namespace EPiServer.Labs.Find.Toolbox
{
    /// <summary>
    /// The one place where the toolbox's extension methods reach the container. Extension methods cannot get constructor
    /// injection, so they resolve the registered services here - on every search, never cached in static fields, so a
    /// replaced registration (ISynonymProvider, options) is always picked up.
    /// Code that wants explicit dependencies can pass a <see cref="UsingSynonymService"/> to UsingSynonymsImproved().
    /// </summary>
    internal static class ToolboxServices
    {
        public static FindToolboxOptions Options => FindToolboxOptions.Current;

        public static UsingSynonymService SynonymService => ServiceLocator.Current.GetInstance<UsingSynonymService>();
    }
}

#if !NETFRAMEWORK
namespace Microsoft.Extensions.DependencyInjection
{
    using EPiServer.Labs.Find.Toolbox;
    using System;

    public static class FindToolboxServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the Find Toolbox explicitly and optionally configures its options (CMS 12). The services are also
        /// registered by their [ServiceConfiguration]/[Options] attributes; calling this documents the dependency in Startup.
        /// <code>services.AddFindToolbox(o => o.MaxSynonymsPerPhrase = 5);</code>
        /// </summary>
        public static IServiceCollection AddFindToolbox(this IServiceCollection services, Action<FindToolboxOptions>? configure = null)
        {
            if (configure != null)
            {
                services.Configure(configure);
            }

            return services;
        }
    }
}
#endif
