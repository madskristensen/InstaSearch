using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;

namespace InstaSearch.Benchmarks
{
    /// <summary>
    /// Benchmarks for search matching algorithms.
    /// Tests the core search logic independent of VS SDK dependencies.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
    public class SearchAlgorithmBenchmarks
    {
        private List<string> _fileNames;
        private List<string> _fileNamesLower;
        private static readonly char[] WildcardSeparator = { '*' };

        [Params(1000, 10000, 50000)]
        public int FileCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            var extensions = new[] { ".cs", ".xaml", ".json", ".txt", ".xml", ".config", ".csproj", ".sln" };
            var prefixes = new[] { "Program", "Service", "Controller", "Model", "View", "Helper", "Utils", "Config", "Test", "Benchmark" };
            var random = new Random(42); // Deterministic seed for reproducibility

            _fileNames = new List<string>(FileCount);
            _fileNamesLower = new List<string>(FileCount);

            for (int i = 0; i < FileCount; i++)
            {
                var prefix = prefixes[random.Next(prefixes.Length)];
                var ext = extensions[random.Next(extensions.Length)];
                var fileName = $"{prefix}{i:D5}{ext}";
                _fileNames.Add(fileName);
                _fileNamesLower.Add(fileName.ToLowerInvariant());
            }
        }

        [Benchmark(Description = "Substring search (IndexOf)")]
        public int SubstringSearch()
        {
            var query = "service";
            var count = 0;
            foreach (var fileName in _fileNamesLower)
            {
                if (fileName.IndexOf(query, StringComparison.Ordinal) >= 0)
                    count++;
            }
            return count;
        }

        [Benchmark(Description = "Wildcard search (*.cs)")]
        public int WildcardSearchExtension()
        {
            var pattern = "*.cs";
            var count = 0;
            foreach (var fileName in _fileNamesLower)
            {
                if (MatchesWildcard(fileName, pattern))
                    count++;
            }
            return count;
        }

        [Benchmark(Description = "Wildcard search (service*.cs)")]
        public int WildcardSearchPrefixAndExtension()
        {
            var pattern = "service*.cs";
            var count = 0;
            foreach (var fileName in _fileNamesLower)
            {
                if (MatchesWildcard(fileName, pattern))
                    count++;
            }
            return count;
        }

        [Benchmark(Description = "Wildcard search (*test*)")]
        public int WildcardSearchContains()
        {
            var pattern = "*test*";
            var count = 0;
            foreach (var fileName in _fileNamesLower)
            {
                if (MatchesWildcard(fileName, pattern))
                    count++;
            }
            return count;
        }

        [Benchmark(Description = "LINQ filter + sort + take")]
        public int LinqFilterSortTake()
        {
            var query = "service";
            return _fileNamesLower
                .Where(f => f.IndexOf(query, StringComparison.Ordinal) >= 0)
                .OrderBy(f => !f.StartsWith(query, StringComparison.Ordinal))
                .ThenBy(f => f)
                .Take(100)
                .Count();
        }

        /// <summary>
        /// Fast wildcard matching without regex. Splits pattern by '*' and checks segments exist in order.
        /// This is the same algorithm used in SearchService.
        /// </summary>
        private static bool MatchesWildcard(string fileName, string pattern)
        {
            var segments = pattern.Split(WildcardSeparator, StringSplitOptions.None);
            var pos = 0;

            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment.Length == 0)
                    continue;

                var index = fileName.IndexOf(segment, pos, StringComparison.Ordinal);
                if (index < 0)
                    return false;

                // First segment must match at start if pattern doesn't start with *
                if (i == 0 && pattern[0] != '*' && index != 0)
                    return false;

                pos = index + segment.Length;
            }

            // Last segment must match at end if pattern doesn't end with *
            if (segments.Length > 0 && pattern[pattern.Length - 1] != '*')
            {
                var lastSegment = segments[segments.Length - 1];
                if (lastSegment.Length > 0 && !fileName.EndsWith(lastSegment, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
