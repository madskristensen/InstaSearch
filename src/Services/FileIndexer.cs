using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InstaSearch.Services
{
    /// <summary>
    /// Fast parallel file system indexer with caching and ignore patterns.
    /// </summary>
    public class FileIndexer
    {
        private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
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

        private readonly ConcurrentDictionary<string, List<FileEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Indexes all files under the given root directory using parallel enumeration.
        /// Results are cached per root directory.
        /// </summary>
        public async Task<IReadOnlyList<FileEntry>> IndexAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                return Array.Empty<FileEntry>();
            }

            rootPath = Path.GetFullPath(rootPath);

            if (_cache.TryGetValue(rootPath, out List<FileEntry> cached))
            {
                return cached;
            }

            List<FileEntry> files = await Task.Run(() => IndexDirectory(rootPath, cancellationToken), cancellationToken);
            _cache[rootPath] = files;
            return files;
        }

        /// <summary>
        /// Clears the cache for a specific root or all roots.
        /// </summary>
        public void InvalidateCache(string rootPath = null)
        {
            if (rootPath == null)
            {
                _cache.Clear();
            }
            else
            {
                _cache.TryRemove(Path.GetFullPath(rootPath), out _);
            }
        }

        private List<FileEntry> IndexDirectory(string rootPath, CancellationToken cancellationToken)
        {
            var results = new ConcurrentBag<FileEntry>();
            var directories = new ConcurrentQueue<string>();
            directories.Enqueue(rootPath);

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            while (!directories.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentBatch = new List<string>();
                while (directories.TryDequeue(out var dir))
                {
                    currentBatch.Add(dir);
                }

                Parallel.ForEach(currentBatch, parallelOptions, directory =>
                {
                    ProcessDirectory(directory, rootPath, directories, results, cancellationToken);
                });
            }

            return results.ToList();
        }

        private void ProcessDirectory(
            string directory,
            string rootPath,
            ConcurrentQueue<string> directories,
            ConcurrentBag<FileEntry> results,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Get and enqueue subdirectories (excluding ignored ones)
                foreach (var subDir in Directory.EnumerateDirectories(directory))
                {
                    var dirName = Path.GetFileName(subDir);
                    if (!IgnoredDirectories.Contains(dirName))
                    {
                        directories.Enqueue(subDir);
                    }
                }

                // Add all files in this directory
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var fileName = Path.GetFileName(file);
                    var relativePath = GetRelativePath(rootPath, file);
                    results.Add(new FileEntry(fileName, file, relativePath));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
            catch (DirectoryNotFoundException)
            {
                // Directory was deleted during enumeration
            }
        }

        private static string GetRelativePath(string rootPath, string fullPath)
        {
            if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = fullPath.Substring(rootPath.Length);
                return relative.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return fullPath;
        }
    }

    /// <summary>
    /// Represents an indexed file entry.
    /// </summary>
    public class FileEntry
    {
        public FileEntry(string fileName, string fullPath, string relativePath)
        {
            FileName = fileName;
            FullPath = fullPath;
            RelativePath = relativePath;
            FileNameLower = fileName.ToLowerInvariant();
        }

        public string FileName { get; }
        public string FullPath { get; }
        public string RelativePath { get; }
        public string FileNameLower { get; }
    }
}
