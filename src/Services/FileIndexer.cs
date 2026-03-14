using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InstaSearch.Services
{
    /// <summary>
    /// Fast parallel file system indexer with caching, ignore patterns, and live file watching.
    /// </summary>
    /// <remarks>
    /// Creates a FileIndexer that uses the provided function to get ignored directories.
    /// </remarks>
    /// <param name="getIgnoredDirectoriesFunc">Function that returns the ignored folder filter.</param>
    public class FileIndexer(Func<IgnoredFolderFilter> getIgnoredDirectoriesFunc) : IDisposable
    {
        /// <summary>
        /// Default folders to ignore during file indexing.
        /// </summary>
        private static readonly IgnoredFolderFilter _defaultIgnoredFilter = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git", ".vs", ".idea", "bin", "obj", "node_modules", "packages",
                ".nuget", "TestResults", "Debug", "Release", ".svn", ".hg"
            },
            []);

        // Cached ignored directories - refreshed on each index operation
        private IgnoredFolderFilter _ignoredFilter;
        private readonly object _ignoredDirectoriesLock = new();

        // Use IReadOnlyList to prevent mutation after caching
        private readonly ConcurrentDictionary<string, IReadOnlyList<FileEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _dirtyFlags = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _indexSemaphores = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, object> _cacheUpdateLocks = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        /// <summary>
        /// Creates a FileIndexer with default ignored directories.
        /// </summary>
        public FileIndexer() : this(null)
        {
        }

        /// <summary>
        /// Gets the current ignored folder filter.
        /// </summary>
        private IgnoredFolderFilter GetIgnoredFilter()
        {
            lock (_ignoredDirectoriesLock)
            {
                // Use the provided function, or fall back to defaults
                _ignoredFilter = getIgnoredDirectoriesFunc?.Invoke() ?? _defaultIgnoredFilter;
                return _ignoredFilter;
            }
        }

        /// <summary>
        /// Indexes all files under the given root directory using parallel enumeration. Results are cached per root
        /// directory and kept up-to-date via FileSystemWatcher.
        /// </summary>
        public async Task<IReadOnlyList<FileEntry>> IndexAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                return [];
            }

            rootPath = Path.GetFullPath(rootPath);

            // Fast path: cache hit and not dirty
            if (_cache.TryGetValue(rootPath, out IReadOnlyList<FileEntry> cached) && !IsDirty(rootPath))
            {
                return cached;
            }

            // Slow path: need to re-index
            // Use per-root SemaphoreSlim for async-friendly locking without serializing different roots.
            var rootSemaphore = _indexSemaphores.GetOrAdd(rootPath, _ => new SemaphoreSlim(1, 1));
            await rootSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock
                if (_cache.TryGetValue(rootPath, out cached) && !IsDirty(rootPath))
                {
                    return cached;
                }

                // Mark as not dirty before indexing to avoid race
                _dirtyFlags[rootPath] = false;

                // Run indexing on thread pool without blocking
                List<FileEntry> files = await Task.Run(() => IndexDirectory(rootPath, cancellationToken), cancellationToken).ConfigureAwait(false);
                _cache[rootPath] = files;

                // Start watching for changes after initial index (idempotent)
                StartWatching(rootPath);

                return files;
            }
            finally
            {
                rootSemaphore.Release();
            }
        }

        private bool IsDirty(string rootPath)
        {
            return _dirtyFlags.TryGetValue(rootPath, out var dirty) && dirty;
        }

        private void MarkDirty(string rootPath)
        {
            _dirtyFlags[rootPath] = true;
        }

        /// <summary>
        /// Clears the cache for a specific root or all roots and stops watching.
        /// </summary>
        public void InvalidateCache(string rootPath = null)
        {
            if (rootPath == null)
            {
                _cache.Clear();
                _dirtyFlags.Clear();
                StopAllWatching();
            }
            else
            {
                var normalizedPath = Path.GetFullPath(rootPath);
                _cache.TryRemove(normalizedPath, out _);
                _dirtyFlags.TryRemove(normalizedPath, out _);
                _cacheUpdateLocks.TryRemove(normalizedPath, out _);
                StopWatching(normalizedPath);
            }
        }

        #region FileSystemWatcher

        private void StartWatching(string rootPath)
        {
            if (_disposed || _watchers.ContainsKey(rootPath))
                return;

            try
            {
                var watcher = new FileSystemWatcher(rootPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 65536 // 64KB buffer to reduce overflow risk
                };

                // Store rootPath in watcher.Site.Name or use a wrapper to avoid closure allocation
                // Use a single handler method to reduce allocations
                watcher.Created += Watcher_OnChanged;
                watcher.Deleted += Watcher_OnChanged;
                watcher.Renamed += Watcher_OnRenamed;
                watcher.Error += Watcher_OnError;

                watcher.EnableRaisingEvents = true;
                _watchers[rootPath] = watcher;
            }
            catch (Exception)
            {
                // Watcher failed to start (e.g., network drive, permissions)
                // Fall back to cache-only mode - searches still work, just not live
            }
        }

        private void Watcher_OnChanged(object sender, FileSystemEventArgs e)
        {
            if (sender is FileSystemWatcher watcher)
            {
                if (e.ChangeType == WatcherChangeTypes.Created)
                {
                    OnFileCreated(watcher.Path, e.FullPath);
                }
                else if (e.ChangeType == WatcherChangeTypes.Deleted)
                {
                    OnFileDeleted(watcher.Path, e.FullPath);
                }
            }
        }

        private void Watcher_OnRenamed(object sender, RenamedEventArgs e)
        {
            if (sender is FileSystemWatcher watcher)
            {
                OnFileRenamed(watcher.Path, e.OldFullPath, e.FullPath);
            }
        }

        private void Watcher_OnError(object sender, ErrorEventArgs e)
        {
            if (sender is FileSystemWatcher watcher)
            {
                MarkDirty(watcher.Path);
            }
        }

        private void OnFileCreated(string rootPath, string fullPath)
        {
            // Ignore changes in excluded directories (optimized to reduce allocations)
            if (IsInIgnoredDirectory(rootPath, fullPath, GetIgnoredFilter()))
                return;

            if (!TryApplyCreatedFileUpdate(rootPath, fullPath))
            {
                MarkDirty(rootPath);
            }
        }

        private void OnFileDeleted(string rootPath, string fullPath)
        {
            if (IsInIgnoredDirectory(rootPath, fullPath, GetIgnoredFilter()))
                return;

            if (!TryApplyDeletedFileUpdate(rootPath, fullPath))
            {
                MarkDirty(rootPath);
            }
        }

        private void OnFileRenamed(string rootPath, string oldFullPath, string newFullPath)
        {
            if (IsInIgnoredDirectory(rootPath, oldFullPath, GetIgnoredFilter())
                && IsInIgnoredDirectory(rootPath, newFullPath, GetIgnoredFilter()))
            {
                return;
            }

            if (!TryApplyRenamedFileUpdate(rootPath, oldFullPath, newFullPath))
            {
                MarkDirty(rootPath);
            }
        }

        private bool TryApplyCreatedFileUpdate(string rootPath, string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return false;
            }

            object updateLock = _cacheUpdateLocks.GetOrAdd(rootPath, _ => new object());

            lock (updateLock)
            {
                if (!_cache.TryGetValue(rootPath, out IReadOnlyList<FileEntry> cached) || IsDirty(rootPath))
                {
                    return false;
                }

                if (cached.Any(entry => string.Equals(entry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                var updated = new List<FileEntry>(cached.Count + 1);
                updated.AddRange(cached);
                updated.Add(new FileEntry(Path.GetFileName(fullPath), fullPath, GetRelativePath(rootPath, fullPath)));
                _cache[rootPath] = updated;
                return true;
            }
        }

        private bool TryApplyDeletedFileUpdate(string rootPath, string fullPath)
        {
            object updateLock = _cacheUpdateLocks.GetOrAdd(rootPath, _ => new object());

            lock (updateLock)
            {
                if (!_cache.TryGetValue(rootPath, out IReadOnlyList<FileEntry> cached) || IsDirty(rootPath))
                {
                    return false;
                }

                var existingIndex = -1;
                for (var i = 0; i < cached.Count; i++)
                {
                    if (string.Equals(cached[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        existingIndex = i;
                        break;
                    }
                }

                if (existingIndex < 0)
                {
                    return true;
                }

                var updated = new List<FileEntry>(cached.Count - 1);
                for (var i = 0; i < cached.Count; i++)
                {
                    if (i != existingIndex)
                    {
                        updated.Add(cached[i]);
                    }
                }

                _cache[rootPath] = updated;
                return true;
            }
        }

        private bool TryApplyRenamedFileUpdate(string rootPath, string oldFullPath, string newFullPath)
        {
            if (!File.Exists(newFullPath))
            {
                return false;
            }

            object updateLock = _cacheUpdateLocks.GetOrAdd(rootPath, _ => new object());

            lock (updateLock)
            {
                if (!_cache.TryGetValue(rootPath, out IReadOnlyList<FileEntry> cached) || IsDirty(rootPath))
                {
                    return false;
                }

                var updated = new List<FileEntry>(cached.Count);
                var removed = false;
                foreach (FileEntry entry in cached)
                {
                    if (string.Equals(entry.FullPath, oldFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        removed = true;
                        continue;
                    }

                    if (string.Equals(entry.FullPath, newFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    updated.Add(entry);
                }

                if (!removed)
                {
                    return false;
                }

                updated.Add(new FileEntry(Path.GetFileName(newFullPath), newFullPath, GetRelativePath(rootPath, newFullPath)));
                _cache[rootPath] = updated;
                return true;
            }
        }

        private void StopWatching(string rootPath)
        {
            if (_watchers.TryRemove(rootPath, out FileSystemWatcher watcher))
            {
                // Unsubscribe to prevent leaks
                watcher.Created -= Watcher_OnChanged;
                watcher.Deleted -= Watcher_OnChanged;
                watcher.Renamed -= Watcher_OnRenamed;
                watcher.Error -= Watcher_OnError;
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }

        private void StopAllWatching()
        {
            foreach (var rootPath in _watchers.Keys.ToList())
            {
                StopWatching(rootPath);
            }
        }

        private static bool IsInIgnoredDirectory(string rootPath, string fullPath, IgnoredFolderFilter ignoredFilter)
        {
            // Optimized: scan the path without allocating substrings for exact matches,
            // but extract segment string when wildcard patterns are present
            var startIndex = rootPath.Length;
            if (startIndex < fullPath.Length && (fullPath[startIndex] == Path.DirectorySeparatorChar || fullPath[startIndex] == Path.AltDirectorySeparatorChar))
            {
                startIndex++;
            }

            var hasWildcards = ignoredFilter.WildcardPatterns.Count > 0;
            var segmentStart = startIndex;
            for (var i = startIndex; i <= fullPath.Length; i++)
            {
                if (i == fullPath.Length || fullPath[i] == Path.DirectorySeparatorChar || fullPath[i] == Path.AltDirectorySeparatorChar)
                {
                    if (i > segmentStart)
                    {
                        var segmentLength = i - segmentStart;

                        // Check exact matches without allocating
                        foreach (var ignored in ignoredFilter.ExactNames)
                        {
                            if (ignored.Length == segmentLength &&
                                string.Compare(fullPath, segmentStart, ignored, 0, segmentLength, StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                return true;
                            }
                        }

                        // Check wildcard patterns (requires extracting the segment)
                        if (hasWildcards)
                        {
                            var segment = fullPath.Substring(segmentStart, segmentLength);
                            if (ignoredFilter.IsIgnored(segment))
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

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            GC.SuppressFinalize(this);
            _disposed = true;
            StopAllWatching();
            _cache.Clear();
            _dirtyFlags.Clear();

            foreach (SemaphoreSlim semaphore in _indexSemaphores.Values)
            {
                semaphore.Dispose();
            }

            _indexSemaphores.Clear();
            _cacheUpdateLocks.Clear();
        }

        #endregion

        private List<FileEntry> IndexDirectory(string rootPath, CancellationToken cancellationToken)
        {
            // Get the current ignored folder filter from options
            IgnoredFolderFilter ignoredFilter = GetIgnoredFilter();

            // Use ConcurrentBag instead of ConcurrentQueue - better for parallel add scenarios
            // and allows direct conversion to List without dequeue loop
            var results = new ConcurrentBag<FileEntry>();
            var pendingCount = 1; // Track outstanding work items
            var directories = new BlockingCollection<string>
            {
                { rootPath, cancellationToken }
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
                                if (cancellationToken.IsCancellationRequested)
                                    break;

                                var dirName = Path.GetFileName(subDir);
                                if (!ignoredFilter.IsIgnored(dirName))
                                {
                                    Interlocked.Increment(ref pendingCount);
                                    directories.Add(subDir, cancellationToken);
                                }
                            }

                            // Process files in this directory
                            foreach (var file in Directory.EnumerateFiles(directory))
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    break;

                                var fileName = Path.GetFileName(file);
                                var relativePath = GetRelativePath(rootPath, file);
                                results.Add(new FileEntry(fileName, file, relativePath));
                            }
                        }
                        catch (UnauthorizedAccessException) { }
                        catch (DirectoryNotFoundException) { }
                        catch (PathTooLongException) { }
                        catch (IOException) { }
                        catch (System.Security.SecurityException) { }

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

            // Direct conversion - ConcurrentBag.ToList() is more efficient than dequeue loop
            return [.. results];
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
    /// Holds both exact folder names and wildcard patterns for folder filtering.
    /// </summary>
    public class IgnoredFolderFilter(HashSet<string> exactNames, IReadOnlyList<string> wildcardPatterns)
    {
        public HashSet<string> ExactNames { get; } = exactNames;
        public IReadOnlyList<string> WildcardPatterns { get; } = wildcardPatterns;

        /// <summary>
        /// Checks if a folder name should be ignored (exact match or wildcard match).
        /// </summary>
        public bool IsIgnored(string folderName)
        {
            if (ExactNames.Contains(folderName))
            {
                return true;
            }

            foreach (var pattern in WildcardPatterns)
            {
                if (MatchesWildcard(folderName, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Matches a folder name against a simple wildcard pattern (e.g., *.Migrations). Supports leading wildcard:
        /// *.suffix
        /// </summary>
        private static bool MatchesWildcard(string folderName, string pattern)
        {
            if (pattern.Length == 0)
            {
                return false;
            }

            if (pattern[0] == '*')
            {
                var suffix = pattern.Substring(1);
                return folderName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }

            return folderName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
