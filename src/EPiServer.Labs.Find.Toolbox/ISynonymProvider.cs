using System;
using System.Collections.Generic;

namespace EPiServer.Labs.Find.Toolbox
{
    /// <summary>
    /// Provides the flattened synonym dictionary used by UsingSynonymsImproved().
    /// Keys are lower case phrases; values are the synonyms for that phrase.
    /// The default implementation is <see cref="SynonymLoader"/>, which reads synonyms from the Find index.
    /// </summary>
    public interface ISynonymProvider
    {
        Dictionary<string, HashSet<string>> GetSynonyms(TimeSpan? cacheDuration = null);
    }
}
