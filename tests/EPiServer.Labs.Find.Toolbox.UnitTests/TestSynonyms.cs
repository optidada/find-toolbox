using EPiServer.Find.Optimizations.Synonyms.Api;

namespace EPiServer.Labs.Find.Toolbox.UnitTests
{
    public static class TestSynonyms
    {
        public static Synonym Create(string phrase, string synonym, bool bidirectional)
            => new Synonym(phrase, synonym, bidirectional);
    }
}
