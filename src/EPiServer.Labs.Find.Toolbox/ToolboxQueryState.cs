namespace EPiServer.Labs.Find.Toolbox
{
    /// <summary>
    /// What the toolbox extensions know about the main query and pass on to each other (not sent to Find).
    /// </summary>
    public sealed class ToolboxQueryState
    {
        /// <summary>
        /// The query has ONE top level clause: one word, or one synonym group such as <c>(find OR (search AND navigation))</c>.
        /// minimum_should_match is then not sent: there is nothing to count, and it would count the wrong things - the
        /// alternatives of an unwrapped synonym group (100% => find AND search AND navigation), or the tokens the analyzer
        /// makes of a single word (automobile => automobile + automobil).
        /// </summary>
        public bool IsSingleClause { get; set; }

        /// <summary>The user's AND / OR / NOT have been removed from the query text (BooleanOperatorHandling.Ignore).</summary>
        public bool BooleanOperatorsRemoved { get; set; }

        /// <summary>
        /// Plain text variations of the query (the user's words first, then synonym variations) set by UsingSynonymsImproved()
        /// and used by UsingRelevanceImproved() for its phrase boosts.
        /// </summary>
        public string[]? QueriesForMatch { get; set; }

        public ToolboxQueryState Clone() => (ToolboxQueryState)MemberwiseClone();
    }
}
