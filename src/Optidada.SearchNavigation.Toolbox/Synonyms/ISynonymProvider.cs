using System;
using System.Collections.Generic;

namespace Optidada.SearchNavigation.Toolbox.Synonyms
{
    /// <summary>
    /// Provides the flattened synonym dictionary used by UsingSynonymsImproved().
    /// Keys are lower case phrases; values are the synonyms for that phrase.
    /// The default implementation is <see cref="FindSynonymProvider"/>, which reads synonyms from the Find index.
    /// </summary>
    public interface ISynonymProvider
    {
        /// <summary>The synonyms: lower case phrase => its synonyms.</summary>
        /// <param name="cacheDuration">How long the synonyms may be cached; null = <see cref="ToolboxOptions.SynonymCacheDuration"/>.</param>
        Dictionary<string, HashSet<string>> GetSynonyms(TimeSpan? cacheDuration = null);
    }
}
