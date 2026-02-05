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
        private static readonly char[] WildcardSeparator = ['*'];

        // Pre-parsed patterns for benchmarks
        private WildcardPattern _patternExtension;
        private WildcardPattern _patternPrefixAndExtension;
        private WildcardPattern _patternContains;

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

            // Pre-parse patterns once - same approach as optimized SearchService
            _patternExtension = new WildcardPattern("*.cs");
            _patternPrefixAndExtension = new WildcardPattern("service*.cs");
            _patternContains = new WildcardPattern("*test*");
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
            var count = 0;
            foreach (var fileName in _fileNamesLower)
            {
                if (_patternExtension.Matches(fileName))
                    count++;
            }
            return count;
        }

        [Benchmark(Description = "Wildcard search (service*.cs)")]
        public int WildcardSearchPrefixAndExtension()
        {
            var count = 0;
            foreach (var fileName in _fileNamesLower)
            {
                if (_patternPrefixAndExtension.Matches(fileName))
                    count++;
            }
            return count;
        }

        [Benchmark(Description = "Wildcard search (*test*)")]
        public int WildcardSearchContains()
        {
            var count = 0;
            foreach (var fileName in _fileNamesLower)
            {
                if (_patternContains.Matches(fileName))
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
        /// Pre-parsed wildcard pattern that avoids allocations during matching.
        /// Parse once, match many times. Same as SearchService.WildcardPattern.
        /// </summary>
        private readonly struct WildcardPattern
        {
            public readonly string[] Segments;
            public readonly bool StartsWithWildcard;
            public readonly bool EndsWithWildcard;

            public WildcardPattern(string pattern)
            {
                Segments = pattern.Split(WildcardSeparator, StringSplitOptions.None);
                StartsWithWildcard = pattern.Length > 0 && pattern[0] == '*';
                EndsWithWildcard = pattern.Length > 0 && pattern[pattern.Length - 1] == '*';
            }

            /// <summary>
            /// Matches the pre-parsed pattern against a filename. Zero allocations.
            /// </summary>
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

                    // First segment must match at start if pattern doesn't start with *
                    if (i == 0 && !StartsWithWildcard && index != 0)
                        return false;

                    pos = index + segment.Length;
                }

                // Last segment must match at end if pattern doesn't end with *
                if (Segments.Length > 0 && !EndsWithWildcard)
                {
                    var lastSegment = Segments[Segments.Length - 1];
                    if (lastSegment.Length > 0 && !fileName.EndsWith(lastSegment, StringComparison.Ordinal))
                        return false;
                }

                return true;
            }
        }
    }
}
