using EPiServer.Find.Optimizations.Synonyms.Api;

using Optidada.SearchNavigation.Toolbox.Queries;
using Optidada.SearchNavigation.Toolbox.Synonyms;

namespace Optidada.SearchNavigation.Toolbox.UnitTests
{
    public static class TestSynonyms
    {
        public static Synonym Create(string phrase, string synonym, bool bidirectional)
            => new Synonym(phrase, synonym, bidirectional);
    }
}
