using EPiServer.ServiceLocation;
using Optidada.SearchNavigation.Toolbox.Synonyms;

namespace Optidada.SearchNavigation.Toolbox
{
    /// <summary>
    /// The one place where the toolbox's extension methods reach the container. Extension methods cannot get constructor
    /// injection, so they resolve the registered services here - on every search, never cached in static fields, so a
    /// replaced registration (ISynonymProvider, options) is always picked up.
    /// Code that wants explicit dependencies can pass a <see cref="SynonymService"/> to UsingSynonymsImproved().
    /// </summary>
    internal static class ToolboxServices
    {
        public static ToolboxOptions Options => ToolboxOptions.Current;

        public static SynonymService SynonymService => ServiceLocator.Current.GetInstance<SynonymService>();
    }
}
