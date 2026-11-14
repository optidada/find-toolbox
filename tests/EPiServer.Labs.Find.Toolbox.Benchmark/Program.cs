// Find Toolbox performance benchmark: indexes a synthetic corpus (sentences from the .NET XML docs, English language
// routing) into the test index and measures the server time ("took") of toolbox query configurations.
// Usage (from this folder): dotnet run -- index | dotnet run -- bench [reps] [config name filter] | dotnet run -- delete
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EPiServer.Find;
using EPiServer.Find.Json;
using EPiServer.Find.Api;
using EPiServer.Find.ClientConventions;
using EPiServer.Labs.Find.Toolbox;

public class PerfDoc
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Body { get; set; }
}

public class DictSynonyms : ISynonymProvider
{
    private readonly Dictionary<string, HashSet<string>> _d;
    public DictSynonyms(Dictionary<string, HashSet<string>> d) => _d = d;
    public Dictionary<string, HashSet<string>> GetSynonyms(TimeSpan? cacheDuration = null) => _d;
}

public static class Program
{
    const int DocCount = 5000;
    static string Filter;
    static readonly Random Rnd = new Random(42);

    public static async Task<int> Main(string[] args)
    {
        var client = CreateClient();
        client.Conventions.ForInstancesOf<PerfDoc>().IdIs(x => x.Id);
        client.Conventions.ForInstancesOf<PerfDoc>().LanguageRoutingIs(x => new LanguageRouting(Language.English));
        var sentences = Sentences();
        Console.WriteLine($"sentences: {sentences.Count}, vocabulary: {sentences.SelectMany(Words).Distinct().Count()}");

        switch (args.FirstOrDefault())
        {
            case "index": await Index(client, sentences); break;
            case "delete": for (var i = 0; i < DocCount; i++) client.Delete<PerfDoc>("perf-" + i); Console.WriteLine("deleted"); break;
            case "count": Console.WriteLine(Count(client)); break;
            default: Filter = args.Length > 2 ? args[2] : null; Bench(client, sentences, args.Length > 1 ? int.Parse(args[1]) : 5); break;
        }
        return 0;
    }

    static IClient CreateClient()
    {
        // FIND_SERVICEURL / FIND_DEFAULTINDEX, or the CMS 12 test site's user secrets. Use a TEST index: "index" writes 5000 documents.
        var serviceUrl = Environment.GetEnvironmentVariable("FIND_SERVICEURL");
        var defaultIndex = Environment.GetEnvironmentVariable("FIND_DEFAULTINDEX");
        if (string.IsNullOrEmpty(serviceUrl) || string.IsNullOrEmpty(defaultIndex))
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", "optidada-alloycms12-toolbox", "secrets.json");
            var secrets = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            serviceUrl = secrets.GetProperty("EPiServer:Find:ServiceUrl").GetString();
            defaultIndex = secrets.GetProperty("EPiServer:Find:DefaultIndex").GetString();
        }
        var options = Microsoft.Extensions.Options.Options.Create(new FindOptions { ServiceUrl = serviceUrl, DefaultIndex = defaultIndex });
        var cache = new EPiServer.Find.Connection.RuntimeCacheAdapter(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient(services);
        var factory = (System.Net.Http.IHttpClientFactory)Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
            .BuildServiceProvider(services).GetService(typeof(System.Net.Http.IHttpClientFactory));
        return new Client(options, cache, factory);
    }

    static List<string> Sentences()
    {
        var dir = Directory.GetDirectories(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref").Where(d => Path.GetFileName(d).StartsWith("8.0")).Max();
        var text = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(dir, "ref", "net8.0"), "*.xml").OrderBy(f => f))
        {
            foreach (var summary in XDocument.Load(file).Descendants("summary"))
            {
                var s = Regex.Replace(summary.Value, @"\s+", " ").Trim();
                if (s.Split(' ').Length >= 6) text.Add(s);
            }
        }
        return text.Distinct().ToList();
    }

    static IEnumerable<string> Words(string s) => Regex.Matches(s.ToLowerInvariant(), "[a-z]{3,}").Select(m => m.Value);

    static async Task Index(IClient client, List<string> sentences)
    {
        // Word like terms from syllables: a large term dictionary (like a big site) with realistic fuzzy neighbours
        var syllables = new[] { "ka","lo","mi","ne","ra","su","te","vi","po","da","ge","hu","ja","ko","li","ma","no","pe","ri","sa","to","va","be","ci","fo","gu","ha","ki","lu","me","ni","or","pa","re","si","ta","un","ve","wi","zo","an","el","in","st","tr","br","pl","gr","ch","sh" };
        var pseudo = new HashSet<string>();
        while (pseudo.Count < 120000) pseudo.Add(string.Concat(Enumerable.Range(0, Rnd.Next(2, 5)).Select(_ => syllables[Rnd.Next(syllables.Length)])));
        var pseudoWords = pseudo.ToArray();
        var docs = new List<PerfDoc>();
        for (var i = 0; i < DocCount; i++)
        {
            var title = string.Join(" ", sentences[Rnd.Next(sentences.Count)].Split(' ').Take(Rnd.Next(4, 9)));
            var body = string.Join(" ", Enumerable.Range(0, Rnd.Next(5, 12)).Select(_ => sentences[Rnd.Next(sentences.Count)]))
                + " " + string.Join(" ", Enumerable.Range(0, 40).Select(_ => pseudoWords[Rnd.Next(pseudoWords.Length)]));
            docs.Add(new PerfDoc { Id = "perf-" + i, Title = title, Body = body });
        }
        foreach (var batch in docs.Chunk(250))
        {
            client.Index(batch);
            Console.Write(".");
            await Task.Delay(300);
        }
        Console.WriteLine();
        for (var i = 0; i < 30 && Count(client) < DocCount; i++) await Task.Delay(2000);
        Console.WriteLine($"indexed: {Count(client)}");
    }

    static int Count(IClient client) { var buster = Guid.NewGuid().ToString(); return client.Search<PerfDoc>().Filter(Buster()).Take(0).GetResult().TotalMatching; }

    // ---------- benchmark ----------

    record QuerySet(string Name, string[] Queries);
    record Config(string Name, FindToolboxOptions Options, Func<string, ITypeSearch<PerfDoc>> Build);

    static void Bench(IClient client, List<string> sentences, int reps)
    {
        var vocabulary = sentences.SelectMany(Words).Distinct().ToArray();
        string Phrase(int n)
        {
            while (true)
            {
                var w = Words(sentences[Rnd.Next(sentences.Count)]).Where(x => x.Length >= 4).Distinct().Take(n).ToArray();
                if (w.Length == n) return string.Join(" ", w);
            }
        }
        string Typo(string q) => string.Join(" ", q.Split(' ').Select(w => w.Length >= 5 ? w.Substring(0, 2) + w[3] + w[2] + w.Substring(4) : w));

        var sets = new List<QuerySet>
        {
            new("1 word", Enumerable.Range(0, 4).Select(_ => Phrase(1)).ToArray()),
            new("2 words", Enumerable.Range(0, 4).Select(_ => Phrase(2)).ToArray()),
            new("3 words", Enumerable.Range(0, 4).Select(_ => Phrase(3)).ToArray()),
            new("5 words typos", Enumerable.Range(0, 4).Select(_ => Typo(Phrase(5))).ToArray()),
            new("10 words", Enumerable.Range(0, 3).Select(_ => Phrase(10)).ToArray()),
        };

        // Synonyms: 3 per query word (some two word synonyms) + 5000 unrelated entries, like a big synonym list
        var synonyms = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string a, string b) { if (!synonyms.TryGetValue(a, out var s)) synonyms[a] = s = new HashSet<string>(StringComparer.OrdinalIgnoreCase); s.Add(b); }
        foreach (var w in sets.SelectMany(s => s.Queries).SelectMany(q => q.Split(' ')).Distinct())
        {
            for (var i = 0; i < 3; i++) Add(w, i == 2 ? vocabulary[Rnd.Next(vocabulary.Length)] + " " + vocabulary[Rnd.Next(vocabulary.Length)] : vocabulary[Rnd.Next(vocabulary.Length)]);
        }
        for (var i = 0; i < 5000; i++) Add(vocabulary[Rnd.Next(vocabulary.Length)], vocabulary[Rnd.Next(vocabulary.Length)]);
        var manySynonyms = synonyms.ToDictionary(p => p.Key, p => new HashSet<string>(p.Value.Concat(Enumerable.Range(0, 20).Select(_ => vocabulary[Rnd.Next(vocabulary.Length)])), StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        var d = new FindToolboxOptions();
        FindToolboxOptions With(Action<FindToolboxOptions> change) { var o = new FindToolboxOptions(); change(o); return o; }
        UsingSynonymService Syn(FindToolboxOptions o, Dictionary<string, HashSet<string>> dict = null) => new UsingSynonymService(new DictSynonyms(dict ?? synonyms), o);

        IQueriedSearch<PerfDoc, EPiServer.Find.Api.Querying.Queries.QueryStringQuery> Plain(string q) => client.Search<PerfDoc>(Language.English).For(q).InField(x => x.Title).InField(x => x.Body);
        ITypeSearch<PerfDoc> Full(string q, FindToolboxOptions o, Dictionary<string, HashSet<string>> dict = null) =>
            client.Search<PerfDoc>(Language.English).For(q).InField(x => x.Title).InField(x => x.Body)
                .UsingSynonymsImproved(Syn(o, dict)).MinimumShouldMatch("2<60%")
                .UsingRelevanceImproved(x => x.Title, x => x.Body)
                .FuzzyMatch(x => x.Title, x => x.Body).WildcardMatch(x => x.Title, x => x.Body);

        var configs = new List<Config>
        {
            new("For() + InField (baseline)", d, Plain),
            new("+ UsingSynonymsImproved", d, q => Plain(q).UsingSynonymsImproved(Syn(d))),
            new("+ synonyms + MSM", d, q => Plain(q).UsingSynonymsImproved(Syn(d)).MinimumShouldMatch("2<60%")),
            new("+ UsingRelevanceImproved", d, q => Plain(q).UsingRelevanceImproved(x => x.Title, x => x.Body)),
            new("+ FuzzyMatch (defaults)", d, q => Plain(q).FuzzyMatch(x => x.Title, x => x.Body)),
            new("+ FuzzyMatch prefix 0", With(o => o.FuzzyPrefixLength = 0), q => Plain(q).FuzzyMatch(x => x.Title, x => x.Body)),
            new("+ FuzzyMatch prefix 2", With(o => o.FuzzyPrefixLength = 2), q => Plain(q).FuzzyMatch(x => x.Title, x => x.Body)),
            new("+ FuzzyMatch no language field", With(o => o.FuzzyUseLanguageField = false), q => Plain(q).FuzzyMatch(x => x.Title, x => x.Body)),
            new("+ FuzzyMatch 10 terms", With(o => o.MaxFuzzyWildcardTerms = 10), q => Plain(q).FuzzyMatch(x => x.Title, x => x.Body)),
            new("+ WildcardMatch (defaults)", d, q => Plain(q).WildcardMatch(x => x.Title, x => x.Body)),
            new("+ WildcardMatch min length 2", With(o => o.MinFuzzyWildcardTermLength = 2), q => Plain(q).WildcardMatch(x => x.Title, x => x.Body)),
            new("+ WildcardMatch 10 terms", With(o => o.MaxFuzzyWildcardTerms = 10), q => Plain(q).WildcardMatch(x => x.Title, x => x.Body)),
            new("+ WildcardMatch double sided", d, q => Plain(q).WildcardMatch(0.2, true, x => x.Title, x => x.Body)),
            new("Full chain (defaults)", d, q => Full(q, d)),
            new("Full chain, 20+ synonyms per word", With(o => o.MaxSynonymsPerPhrase = 30), q => Full(q, With(o => o.MaxSynonymsPerPhrase = 30), manySynonyms)),
            new("Full chain, no limits", With(o => { o.MaxSynonymsPerPhrase = 30; o.MaxExpandedPhrases = 50; o.MaxFuzzyWildcardTerms = 50; o.MinFuzzyWildcardTermLength = 2; o.FuzzyPrefixLength = 0; o.MaxRelevanceTerms = 50; }),
                q => Full(q, With(o => { o.MaxSynonymsPerPhrase = 30; o.MaxExpandedPhrases = 50; o.MaxFuzzyWildcardTerms = 50; o.MinFuzzyWildcardTermLength = 2; o.FuzzyPrefixLength = 0; o.MaxRelevanceTerms = 50; }), manySynonyms)),
        };

        var overrideMethod = typeof(FindToolboxOptions).GetMethod("Override", BindingFlags.Static | BindingFlags.NonPublic);
        var rows = new List<string[]>();
        var json = new List<object>();
        foreach (var config in configs)
        {
            if (Filter != null && !config.Name.Contains(Filter)) continue;
            var cells = new List<string> { config.Name };
            foreach (var set in sets)
            {
                var took = new List<double>();
                var wall = new List<double>();
                var bytes = 0;
                var hits = 0;
                foreach (var q in set.Queries)
                {
                    for (var r = 0; r <= reps; r++)
                    {
                        using ((IDisposable)overrideMethod.Invoke(null, new object[] { config.Options }))
                        {
                            var buster = Guid.NewGuid().ToString();
                            var search = config.Build(q).Filter(Buster()).Take(10);
                            if (r == 0)
                            {
                                var ctx = new SearchContext();
                                search.ApplyActions(ctx);
                                bytes = Math.Max(bytes, Serializer.Serialize(Serializer.CreateDefault(), ctx.RequestBody).Length);
                            }
                            var sw = Stopwatch.StartNew();
                            EPiServer.Find.SearchResults<PerfDoc> result;
                            try { result = Retry(() => search.GetResult()); }
                            catch (Exception)
                            {
                                var ctx = new SearchContext();
                                search.ApplyActions(ctx);
                                File.WriteAllText("failed.json", Serializer.Serialize(Serializer.CreateDefault(), ctx.RequestBody));
                                Console.WriteLine($"FAILED {config.Name} [{q}] -> failed.json");
                                throw;
                            }
                            sw.Stop();
                            if (r == 0) { hits += result.TotalMatching; continue; } // warm up
                            took.Add(result.ProcessingInfo.ServerDuration);
                            wall.Add(sw.Elapsed.TotalMilliseconds);
                        }
                        // Be gentle with the shared service: 250 ms between requests, more when it gets slow
                        Thread.Sleep(250);
                        if (wall.Count > 0 && wall[wall.Count - 1] > 1500) { Console.WriteLine("  slow response, pausing"); Thread.Sleep(5000); }
                    }
                }
                took.Sort(); wall.Sort();
                var med = took[took.Count / 2];
                var p95 = took[(int)Math.Min(took.Count - 1, Math.Ceiling(took.Count * 0.95) - 1)];
                cells.Add($"{med:0} / {p95:0}");
                json.Add(new { config = config.Name, set = set.Name, tookMedian = med, tookP95 = p95, wallMedian = wall[wall.Count / 2], requestBytes = bytes, hitsPerQuery = hits / set.Queries.Length });
                Console.WriteLine($"{config.Name,-36} {set.Name,-14} took med {med,5:0} p95 {p95,5:0} ms  wall {wall[wall.Count / 2],5:0} ms  body {bytes,6} B  hits {hits / set.Queries.Length}");
            }
            rows.Add(cells.ToArray());
        }

        Console.WriteLine();
        Console.WriteLine("| Configuration | " + string.Join(" | ", sets.Select(s => s.Name)) + " |");
        Console.WriteLine("|---|" + string.Join("|", sets.Select(_ => "---:")) + "|");
        foreach (var row in rows) Console.WriteLine("| " + string.Join(" | ", row) + " |");
        File.WriteAllText("results.json", JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("queries: " + string.Join(" ; ", sets.Select(s => s.Name + ": " + string.Join(", ", s.Queries))));
    }

    // Not matching anything that exists: changes the request (no CDN cache hit) but not the results
    static EPiServer.Find.Api.Querying.Filter Buster() => new EPiServer.Find.Api.Querying.Filters.NotFilter(
        new EPiServer.Find.Api.Querying.Filters.TermFilter("Id$$string", EPiServer.Find.Api.Querying.FieldFilterValue.Create(Guid.NewGuid().ToString())));

    static T Retry<T>(Func<T> f)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return f(); }
            catch (Exception e) when (attempt < 5 && System.Text.RegularExpressions.Regex.IsMatch(e.Message + e.InnerException?.Message, @"\b(429|5\d\d)\b"))
            {
                Console.WriteLine("  backing off: " + e.Message.Split('\n')[0]);
                Thread.Sleep(5000 * (attempt + 1));
            }
        }
    }
}
