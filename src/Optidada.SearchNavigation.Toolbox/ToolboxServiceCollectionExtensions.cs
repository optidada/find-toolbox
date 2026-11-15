#if !NETFRAMEWORK
namespace Microsoft.Extensions.DependencyInjection
{
    using System;
    using Optidada.SearchNavigation.Toolbox;

    /// <summary>Registration of the toolbox in a CMS 12 service collection.</summary>
    public static class ToolboxServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the Find Toolbox explicitly and optionally configures its options (CMS 12). The services are also
        /// registered by their [ServiceConfiguration]/[Options] attributes; calling this documents the dependency in Startup.
        /// <code>services.AddSearchNavigationToolbox(o => o.MaxSynonymsPerPhrase = 5);</code>
        /// </summary>
        public static IServiceCollection AddSearchNavigationToolbox(this IServiceCollection services, Action<ToolboxOptions>? configure = null)
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
