using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;

namespace InstaSearch.Benchmarks
{
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
    [CPUUsageDiagnoser]
    public class SearchHistoryBenchmarks
    {
        private Dictionary<string, int> _selectionCounts;
        private List<string> _filePaths;
        private List<string> _historyPaths;
        private readonly object _lock = new();
        [Params(1000, 10000, 50000)]
        public int FileCount { get; set; }

        [Params(100, 500)]
        public int HistorySize { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _selectionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _filePaths = new List<string>(FileCount);
            _historyPaths = new List<string>(HistorySize);
            var random = new Random(42);
            // Generate file paths
            for (var i = 0; i < FileCount; i++)
            {
                _filePaths.Add($@"C:\Projects\MyApp\src\Services\Service{i:D5}.cs");
            }

            // Populate history with subset of files (simulates real usage)
            for (var i = 0; i < HistorySize && i < FileCount; i++)
            {
                var path = _filePaths[random.Next(FileCount)];
                _historyPaths.Add(path);
                _selectionCounts[path] = random.Next(1, 20);
            }
        }

        /// <summary>
        /// Simulates GetSelectionCount called for every file during search.
        /// This is the hot path - called FileCount times per search.
        /// </summary>
        [Benchmark(Description = "History lookup (all files)")]
        public int HistoryLookupAllFiles()
        {
            var total = 0;
            lock (_lock)
            {
                foreach (var path in _filePaths)
                {
                    if (_selectionCounts.TryGetValue(path, out var count))
                    {
                        total += count;
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// Lock-free version for comparison - measures lock overhead.
        /// </summary>
        [Benchmark(Description = "History lookup (lock-free)")]
        public int HistoryLookupLockFree()
        {
            var total = 0;
            foreach (var path in _filePaths)
            {
                if (_selectionCounts.TryGetValue(path, out var count))
                {
                    total += count;
                }
            }

            return total;
        }

        /// <summary>
        /// Simulates RecordSelection for a batch of selections.
        /// </summary>
        [Benchmark(Description = "Record selection (100 items)")]
        public int RecordSelectionBatch()
        {
            var count = 0;
            lock (_lock)
            {
                for (var i = 0; i < 100; i++)
                {
                    var path = _filePaths[i % _filePaths.Count];
                    if (_selectionCounts.TryGetValue(path, out var existing))
                    {
                        _selectionCounts[path] = existing + 1;
                    }
                    else
                    {
                        _selectionCounts[path] = 1;
                    }

                    count++;
                }
            }

            return count;
        }
    }
}