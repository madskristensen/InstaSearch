using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;

namespace InstaSearch.Benchmarks
{
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
    [CPUUsageDiagnoser]
    public class PathFilteringBenchmarks
    {
        private static readonly HashSet<string> _ignoredDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            ".idea",
            "bin",
            "obj",
            "node_modules",
            "packages",
            ".nuget",
            "TestResults",
            "Debug",
            "Release",
            ".svn",
            ".hg"
        };
        private List<string> _filePaths;
        private string _rootPath;
        [Params(1000, 10000)]
        public int PathCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _rootPath = @"C:\Projects\MyLargeApp";
            _filePaths = new List<string>(PathCount);
            var subDirs = new[]
            {
                "src",
                "tests",
                "docs",
                "bin",
                "obj",
                "node_modules",
                ".git",
                "packages"
            };
            var files = new[]
            {
                "Program.cs",
                "Service.cs",
                "Model.cs",
                "index.js",
                "config.json"
            };
            var random = new Random(42);
            for (var i = 0; i < PathCount; i++)
            {
                var subDir = subDirs[random.Next(subDirs.Length)];
                var file = files[random.Next(files.Length)];
                var depth = random.Next(1, 5);
                var path = _rootPath;
                for (var d = 0; d < depth; d++)
                {
                    path = Path.Combine(path, d == depth - 1 ? subDir : $"Level{d}");
                }

                _filePaths.Add(Path.Combine(path, file));
            }
        }

        /// <summary>
        /// Optimized zero-allocation path segment check (current implementation).
        /// </summary>
        [Benchmark(Description = "IsInIgnoredDirectory (optimized)")]
        public int IsInIgnoredDirectoryOptimized()
        {
            var count = 0;
            foreach (var fullPath in _filePaths)
            {
                if (IsInIgnoredDirectoryOptimizedImpl(_rootPath, fullPath, _ignoredDirectories))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Simple Split-based approach for comparison (allocates strings).
        /// </summary>
        [Benchmark(Description = "IsInIgnoredDirectory (Split-based)", Baseline = true)]
        public int IsInIgnoredDirectorySplit()
        {
            var count = 0;
            foreach (var fullPath in _filePaths)
            {
                if (IsInIgnoredDirectorySplitImpl(_rootPath, fullPath, _ignoredDirectories))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Optimized: scans path segments without allocating substrings.
        /// </summary>
        private static bool IsInIgnoredDirectoryOptimizedImpl(string rootPath, string fullPath, HashSet<string> ignoredDirectories)
        {
            var startIndex = rootPath.Length;
            if (startIndex < fullPath.Length && (fullPath[startIndex] == Path.DirectorySeparatorChar || fullPath[startIndex] == Path.AltDirectorySeparatorChar))
            {
                startIndex++;
            }

            var segmentStart = startIndex;
            for (var i = startIndex; i <= fullPath.Length; i++)
            {
                if (i == fullPath.Length || fullPath[i] == Path.DirectorySeparatorChar || fullPath[i] == Path.AltDirectorySeparatorChar)
                {
                    if (i > segmentStart)
                    {
                        var segmentLength = i - segmentStart;
                        foreach (var ignored in ignoredDirectories)
                        {
                            if (ignored.Length == segmentLength && string.Compare(fullPath, segmentStart, ignored, 0, segmentLength, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                return true;
                            }
                        }
                    }

                    segmentStart = i + 1;
                }
            }

            return false;
        }

        /// <summary>
        /// Simple Split-based implementation for baseline comparison.
        /// </summary>
        private static bool IsInIgnoredDirectorySplitImpl(string rootPath, string fullPath, HashSet<string> ignoredDirectories)
        {
            var relativePath = fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (ignoredDirectories.Contains(segment))
                    return true;
            }

            return false;
        }
    }
}