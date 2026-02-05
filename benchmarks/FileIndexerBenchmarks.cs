using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using InstaSearch.Services;

namespace InstaSearch.Benchmarks
{
    /// <summary>
    /// Benchmarks for FileIndexer performance across different directory sizes.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 5)]
    public class FileIndexerBenchmarks
    {
        private string _testRootPath;
        private FileIndexer _indexer;

        [Params(100, 1000, 5000)]
        public int FileCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            // Create a temporary test directory with files
            _testRootPath = Path.Combine(Path.GetTempPath(), "InstaSearchBenchmark_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_testRootPath);

            // Create nested directory structure with files
            var extensions = new[] { ".cs", ".xaml", ".json", ".txt", ".xml", ".config" };
            var dirCount = FileCount / 10; // ~10 files per directory
            if (dirCount < 1) dirCount = 1;

            for (int d = 0; d < dirCount; d++)
            {
                var subDir = Path.Combine(_testRootPath, $"Dir{d:D4}");
                Directory.CreateDirectory(subDir);

                var filesInDir = FileCount / dirCount;
                for (int f = 0; f < filesInDir; f++)
                {
                    var ext = extensions[(d + f) % extensions.Length];
                    var filePath = Path.Combine(subDir, $"File{f:D4}{ext}");
                    File.WriteAllText(filePath, "// benchmark test file");
                }
            }

            _indexer = new FileIndexer();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _indexer?.Dispose();

            if (Directory.Exists(_testRootPath))
            {
                try
                {
                    Directory.Delete(_testRootPath, recursive: true);
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }
        }

        [IterationSetup]
        public void IterationSetup()
        {
            // Invalidate cache before each iteration to measure cold indexing
            _indexer.InvalidateCache(_testRootPath);
        }

        [Benchmark(Description = "Index directory (cold)")]
        public async Task<int> IndexDirectoryCold()
        {
            var files = await _indexer.IndexAsync(_testRootPath, CancellationToken.None);
            return files.Count;
        }

        [Benchmark(Description = "Index directory (cached)")]
        public async Task<int> IndexDirectoryCached()
        {
            // First call populates cache
            await _indexer.IndexAsync(_testRootPath, CancellationToken.None);
            // Second call hits cache
            var files = await _indexer.IndexAsync(_testRootPath, CancellationToken.None);
            return files.Count;
        }
    }
}
