using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using InstaSearch.Services;

namespace InstaSearch.Benchmarks
{
    /// <summary>
    /// Base class for FileIndexer benchmarks with shared setup/cleanup logic.
    /// </summary>
    public abstract class FileIndexerBenchmarkBase
    {
        protected string TestRootPath;
        protected FileIndexer Indexer;

        [Params(100, 1000, 5000)]
        public int FileCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            // Create a temporary test directory with files
            TestRootPath = Path.Combine(Path.GetTempPath(), "InstaSearchBenchmark_" + Path.GetRandomFileName());
            Directory.CreateDirectory(TestRootPath);

            // Create nested directory structure with files
            var extensions = new[] { ".cs", ".xaml", ".json", ".txt", ".xml", ".config" };
            var dirCount = FileCount / 10; // ~10 files per directory
            if (dirCount < 1) dirCount = 1;

            for (var d = 0; d < dirCount; d++)
            {
                var subDir = Path.Combine(TestRootPath, $"Dir{d:D4}");
                Directory.CreateDirectory(subDir);

                var filesInDir = FileCount / dirCount;
                for (var f = 0; f < filesInDir; f++)
                {
                    var ext = extensions[(d + f) % extensions.Length];
                    var filePath = Path.Combine(subDir, $"File{f:D4}{ext}");
                    File.WriteAllText(filePath, "// benchmark test file");
                }
            }

            Indexer = new FileIndexer();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            Indexer?.Dispose();

            if (Directory.Exists(TestRootPath))
            {
                try
                {
                    Directory.Delete(TestRootPath, recursive: true);
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }
        }
    }

    /// <summary>
    /// Benchmarks for cold (uncached) FileIndexer performance.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
    public class FileIndexerBenchmarks : FileIndexerBenchmarkBase
    {
        [IterationSetup]
        public void IterationSetup()
        {
            // Invalidate cache before each iteration to measure cold indexing
            Indexer.InvalidateCache(TestRootPath);
        }

        [Benchmark(Description = "Index directory (cold)")]
        public async Task<int> IndexDirectoryCold()
        {
            IReadOnlyList<FileEntry> files = await Indexer.IndexAsync(TestRootPath, CancellationToken.None);
            return files.Count;
        }
    }

    /// <summary>
    /// Benchmarks for cached FileIndexer performance.
    /// Cache is populated once per parameter combination in GlobalSetup.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
    public class FileIndexerCachedBenchmarks : FileIndexerBenchmarkBase
    {
        [IterationSetup]
        public void IterationSetup()
        {
            // Pre-populate cache before measuring cache hit performance
            // This runs before each iteration to ensure cache is warm
            Indexer.IndexAsync(TestRootPath, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Benchmark(Description = "Index directory (cached)")]
        public async Task<int> IndexDirectoryCached()
        {
            // This call should hit the cache - measuring cache lookup performance
            IReadOnlyList<FileEntry> files = await Indexer.IndexAsync(TestRootPath, CancellationToken.None);
            return files.Count;
        }
    }
}
