using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace InstaSearch.Services
{
    /// <summary>
    /// Fast parallel file system indexer with caching and ignore patterns.
    /// </summary>
    public class FileIndexer
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

        private readonly ConcurrentDictionary<string, List<FileEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Indexes all files under the given root directory using parallel enumeration.
        /// Results are cached per root directory.
        /// </summary>
        public async Task<IReadOnlyList<FileEntry>> IndexAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                return [];
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
            var results = new ConcurrentQueue<FileEntry>();
            var pendingCount = 1; // Track outstanding work items
            var directories = new BlockingCollection<string>
            {
                rootPath
            };

            // Use a fixed thread pool for continuous work-stealing
            var tasks = new Task[Environment.ProcessorCount];
            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    // Each thread continuously pulls work until no work remains
                    foreach (var directory in directories.GetConsumingEnumerable())
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        try
                        {
                            // Get subdirectories first - this adds more work
                            foreach (var subDir in Directory.EnumerateDirectories(directory))
                            {
                                var dirName = Path.GetFileName(subDir);
                                if (!_ignoredDirectories.Contains(dirName))
                                {
                                    Interlocked.Increment(ref pendingCount);
                                    directories.Add(subDir);
                                }
                            }

                            // Process files in this directory
                            foreach (var file in Directory.EnumerateFiles(directory))
                            {
                                var fileName = Path.GetFileName(file);
                                var relativePath = GetRelativePath(rootPath, file);
                                results.Enqueue(new FileEntry(fileName, file, relativePath));
                            }
                        }
                        catch (UnauthorizedAccessException) { }
                        catch (DirectoryNotFoundException) { }

                        // Decrement pending count; if zero, signal completion
                        if (Interlocked.Decrement(ref pendingCount) == 0)
                        {
                            directories.CompleteAdding();
                        }
                    }
                }, cancellationToken);
            }

            try
            {
                Task.WaitAll(tasks, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                directories.CompleteAdding();
                throw;
            }

            // Convert to list
            var resultList = new List<FileEntry>(results.Count);
            while (results.TryDequeue(out FileEntry entry))
            {
                resultList.Add(entry);
            }
            return resultList;
        }

        // ProcessDirectory is no longer needed - logic moved inline above

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
    public class FileEntry(string fileName, string fullPath, string relativePath)
    {
        public string FileName { get; } = fileName;
        public string FullPath { get; } = fullPath;
        public string RelativePath { get; } = relativePath;
        public string FileNameLower { get; } = fileName.ToLowerInvariant();
    }
}
