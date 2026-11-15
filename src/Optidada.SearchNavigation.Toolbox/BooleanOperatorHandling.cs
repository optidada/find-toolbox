namespace Optidada.SearchNavigation.Toolbox
{
    /// <summary>How upper case AND / OR / NOT in the user's query are treated.</summary>
    public enum BooleanOperatorHandling
    {
        /// <summary>Removed: the query is searched as plain words with all toolbox features (default).</summary>
        Ignore,

        /// <summary>Left to Find as operators; the toolbox then adds nothing to such queries.</summary>
        Respect,
    }
}
