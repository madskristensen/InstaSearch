using BenchmarkDotNet.Attributes;
using InstaSearch.Services;
using Microsoft.VSDiagnostics;

namespace InstaSearch.Benchmarks
{
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
    [CPUUsageDiagnoser]
    public class SearchPipelineBenchmarks
    {
        private List<FileEntry> _files;
        private Dictionary<string, int> _historyScores;
        private static readonly char[] _wildcardSeparator = new[]
        {
            '*'
        };
        [Params(1000, 10000, 50000)]
        public int FileCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            var extensions = new[]
            {
                ".cs",
                ".xaml",
                ".json",
                ".txt",
                ".xml",
                ".config",
                ".csproj",
                ".sln"
            };
            var prefixes = new[]
            {
                "Program",
                "Service",
                "Controller",
                "Model",
                "View",
                "Helper",
                "Utils",
                "Config",
                "Test",
                "Benchmark"
            };
            var random = new Random(42);
            _files = new List<FileEntry>(FileCount);
            _historyScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < FileCount; i++)
            {
                var prefix = prefixes[random.Next(prefixes.Length)];
                var ext = extensions[random.Next(extensions.Length)];
                var fileName = $"{prefix}{i:D5}{ext}";
                var fullPath = $@"C:\Projects\MyApp\src\{prefix}\{fileName}";
                var relativePath = $@"src\{prefix}\{fileName}";
                _files.Add(new FileEntry(fileName, fullPath, relativePath));
                // ~10% of files have history
                if (random.Next(10) == 0)
                {
                    _historyScores[fullPath] = random.Next(1, 20);
                }
            }
        }

        private int GetHistoryScore(string fullPath)
        {
            return _historyScores.TryGetValue(fullPath, out var score) ? score : 0;
        }

        /// <summary>
        /// Ranked file entry for sorting. Using struct to avoid allocations.
        /// </summary>
        private readonly struct RankedFile(FileEntry file, int score, bool startsWithQuery) : IComparable<RankedFile>
        {
            public readonly FileEntry File = file;
            public readonly int Score = score;
            public readonly bool StartsWithQuery = startsWithQuery;

            public int CompareTo(RankedFile other)
            {
                var scoreCompare = other.Score.CompareTo(Score);
                if (scoreCompare != 0) return scoreCompare;
                var startsCompare = other.StartsWithQuery.CompareTo(StartsWithQuery);
                if (startsCompare != 0) return startsCompare;
                return string.Compare(File.FileName, other.File.FileName, StringComparison.Ordinal);
            }
        }

        private static List<FileEntry> SelectTopN(IEnumerable<FileEntry> source, Func<FileEntry, RankedFile> selector, int maxResults)
        {
            var topItems = new List<RankedFile>(maxResults + 1);
            foreach (FileEntry file in source)
            {
                RankedFile ranked = selector(file);
                var insertIndex = topItems.BinarySearch(ranked);
                if (insertIndex < 0) insertIndex = ~insertIndex;
                if (insertIndex < maxResults)
                {
                    topItems.Insert(insertIndex, ranked);
                    if (topItems.Count > maxResults)
                        topItems.RemoveAt(maxResults);
                }
            }
            return topItems.ConvertAll(r => r.File);
        }

        /// <summary>
        /// BASELINE: Full search pipeline with LINQ sort (original implementation).
        /// </summary>
        [Benchmark(Description = "LINQ: substring search", Baseline = true)]
        public int PipelineSubstringSearch()
        {
            var queryLower = "service";
            var maxResults = 100;
            var results = _files.Where(f => f.FileNameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0).Select(f => (File: f, Score: GetHistoryScore(f.FullPath), StartsWithQuery: f.FileNameLower.StartsWith(queryLower, StringComparison.Ordinal))).OrderByDescending(x => x.Score).ThenByDescending(x => x.StartsWithQuery).ThenBy(x => x.File.FileName).Take(maxResults).ToList();
            return results.Count;
        }

        /// <summary>
        /// BASELINE: Full search pipeline with wildcard query (original LINQ).
        /// </summary>
        [Benchmark(Description = "LINQ: wildcard search (*.cs)")]
        public int PipelineWildcardSearch()
        {
            var pattern = new WildcardPattern("*.cs");
            var maxResults = 100;
            var results = _files.Where(f => pattern.Matches(f.FileNameLower)).Select(f => (File: f, Score: GetHistoryScore(f.FullPath), StartsWithQuery: pattern.StartsWithFirstSegment(f.FileNameLower))).OrderByDescending(x => x.Score).ThenByDescending(x => x.StartsWithQuery).ThenBy(x => x.File.FileName).Take(maxResults).ToList();
            return results.Count;
        }

        /// <summary>
        /// OPTIMIZED: Substring search with SelectTopN.
        /// </summary>
        [Benchmark(Description = "Optimized: substring search")]
        public int PipelineSubstringSearchOptimized()
        {
            var queryLower = "service";
            var maxResults = 100;
            List<FileEntry> results = SelectTopN(
                _files.Where(f => f.FileNameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0),
                f => new RankedFile(f, GetHistoryScore(f.FullPath), f.FileNameLower.StartsWith(queryLower, StringComparison.Ordinal)),
                maxResults);
            return results.Count;
        }

        /// <summary>
        /// OPTIMIZED: Wildcard search with SelectTopN.
        /// </summary>
        [Benchmark(Description = "Optimized: wildcard search (*.cs)")]
        public int PipelineWildcardSearchOptimized()
        {
            var pattern = new WildcardPattern("*.cs");
            var maxResults = 100;
            List<FileEntry> results = SelectTopN(
                _files.Where(f => pattern.Matches(f.FileNameLower)),
                f => new RankedFile(f, GetHistoryScore(f.FullPath), pattern.StartsWithFirstSegment(f.FileNameLower)),
                maxResults);
            return results.Count;
        }

        /// <summary>
        /// BASELINE: Empty query - returns history-only results (original LINQ).
        /// </summary>
        [Benchmark(Description = "LINQ: empty query (history only)")]
        public int PipelineHistoryOnly()
        {
            var maxResults = 100;
            var results = _files.Select(f => (File: f, Score: GetHistoryScore(f.FullPath))).Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.File.FileName).Take(maxResults).ToList();
            return results.Count;
        }

        /// <summary>
        /// OPTIMIZED: Empty query with SelectTopN.
        /// </summary>
        [Benchmark(Description = "Optimized: empty query (history only)")]
        public int PipelineHistoryOnlyOptimized()
        {
            var maxResults = 100;
            List<FileEntry> results = SelectTopN(
                _files.Where(f => GetHistoryScore(f.FullPath) > 0),
                f => new RankedFile(f, GetHistoryScore(f.FullPath), false),
                maxResults);
            return results.Count;
        }

        /// <summary>
        /// Measures just the filtering step (no sort/rank).
        /// </summary>
        [Benchmark(Description = "Filter only (no sort)")]
        public int FilterOnly()
        {
            var queryLower = "service";
            var count = 0;
            foreach (FileEntry f in _files)
            {
                if (f.FileNameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Pre-parsed wildcard pattern (mirrors SearchService.WildcardPattern).
        /// Duplicated here to avoid VS SDK dependency from SearchService.
        /// </summary>
        private readonly struct WildcardPattern
        {
            public readonly string[] Segments;
            public readonly bool StartsWithWildcard;
            public readonly bool EndsWithWildcard;
            public readonly string FirstSegment;
            public WildcardPattern(string pattern)
            {
                Segments = pattern.Split(_wildcardSeparator, StringSplitOptions.None);
                StartsWithWildcard = pattern.Length > 0 && pattern[0] == '*';
                EndsWithWildcard = pattern.Length > 0 && pattern[pattern.Length - 1] == '*';
                FirstSegment = null;
                foreach (var seg in Segments)
                {
                    if (seg.Length > 0)
                    {
                        FirstSegment = seg;
                        break;
                    }
                }
            }

            public bool Matches(string fileName)
            {
                var pos = 0;
                for (var i = 0; i < Segments.Length; i++)
                {
                    var segment = Segments[i];
                    if (segment.Length == 0)
                        continue;
                    var index = fileName.IndexOf(segment, pos, StringComparison.Ordinal);
                    if (index < 0)
                        return false;
                    if (i == 0 && !StartsWithWildcard && index != 0)
                        return false;
                    pos = index + segment.Length;
                }

                if (Segments.Length > 0 && !EndsWithWildcard)
                {
                    var lastSegment = Segments[Segments.Length - 1];
                    if (lastSegment.Length > 0 && !fileName.EndsWith(lastSegment, StringComparison.Ordinal))
                        return false;
                }

                return true;
            }

            public bool StartsWithFirstSegment(string fileName)
            {
                if (StartsWithWildcard || FirstSegment == null)
                    return false;
                return fileName.StartsWith(FirstSegment, StringComparison.Ordinal);
            }
        }
    }
}