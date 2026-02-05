using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell.Interop;

namespace InstaSearch.Services
{
    /// <summary>
    /// Fast file search service with history-based ranking.
    /// </summary>
    public class SearchService(FileIndexer indexer, SearchHistoryService history)
    {

        /// <summary>
        /// Searches for files matching the query with results ranked by history.
        /// </summary>
        /// <param name="rootPath">The root directory to search in.</param>
        /// <param name="query">The search query (case-insensitive substring match on file name).</param>
        /// <param name="imageService">VS image service for getting file icons.</param>
        /// <param name="maxResults">Maximum number of results to return.</param>
        /// <param name="cancellationToken">Cancellation token for cancelling the search.</param>
        /// <returns>List of matching files, ranked by history then alphabetically.</returns>
        public async Task<IReadOnlyList<SearchResult>> SearchAsync(
            string rootPath,
            string query,
            IVsImageService2 imageService,
            int maxResults = 100,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                return [];
            }

            // Get indexed files
            IReadOnlyList<FileEntry> files = await indexer.IndexAsync(rootPath, cancellationToken);

            IReadOnlyList<FileEntry> rankedFiles;

            if (string.IsNullOrWhiteSpace(query))
            {
                // Show most recently selected files when query is empty
                // Sort and take BEFORE getting monikers (expensive UI operation)
                rankedFiles = [.. files
                    .Select(f => (File: f, Score: history.GetSelectionCount(f.FullPath)))
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.File.FileName)
                    .Take(maxResults)
                    .Select(x => x.File)];
            }
            else
            {
                var queryLower = query.ToLowerInvariant();

                // Check if query contains wildcards
                var hasWildcard = queryLower.Contains('*');

                if (hasWildcard)
                {
                    // Pre-parse wildcard pattern once - avoids allocations during matching
                    var wildcardPattern = new WildcardPattern(queryLower);

                    // Filter, rank, and take BEFORE getting monikers
                    rankedFiles = [.. files
                        .Where(f => wildcardPattern.Matches(f.FileNameLower))
                        .Select(f => (File: f, Score: history.GetSelectionCount(f.FullPath), StartsWithQuery: wildcardPattern.StartsWithFirstSegment(f.FileNameLower)))
                        .OrderByDescending(x => x.Score)
                        .ThenByDescending(x => x.StartsWithQuery)
                        .ThenBy(x => x.File.FileName)
                        .Take(maxResults)
                        .Select(x => x.File)];
                }
                else
                {
                    // Filter, rank, and take BEFORE getting monikers
                    rankedFiles = [.. files
                        .Where(f => f.FileNameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                        .Select(f => (File: f, Score: history.GetSelectionCount(f.FullPath), StartsWithQuery: f.FileNameLower.StartsWith(queryLower, StringComparison.Ordinal)))
                        .OrderByDescending(x => x.Score)
                        .ThenByDescending(x => x.StartsWithQuery)
                        .ThenBy(x => x.File.FileName)
                        .Take(maxResults)
                        .Select(x => x.File)];
                }
            }

            // Only now switch to UI thread to get monikers - only for final results
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Create results with monikers - only for the limited result set
            var results = new List<SearchResult>(rankedFiles.Count);
            foreach (FileEntry f in rankedFiles)
            {
                results.Add(new SearchResult(f, history.GetSelectionCount(f.FullPath), GetMoniker(imageService, f.FileName)));
            }

            return results;
        }

        private static ImageMoniker GetMoniker(IVsImageService2 imageService, string fileName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return imageService.GetImageMonikerForFile(fileName);
        }

        private static readonly char[] _wildcardSeparator = ['*'];

        /// <summary>
        /// Pre-parsed wildcard pattern that avoids allocations during matching.
        /// Parse once, match many times.
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

                // Cache first non-empty segment for StartsWith ranking
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

            /// <summary>
            /// Checks if filename starts with the first segment (for ranking).
            /// </summary>
            public bool StartsWithFirstSegment(string fileName)
            {
                if (StartsWithWildcard || FirstSegment == null)
                    return false;
                return fileName.StartsWith(FirstSegment, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Records that a file was selected and saves history.
        /// </summary>
        public async Task RecordSelectionAsync(string fullPath)
        {
            history.RecordSelection(fullPath);
            await history.SaveAsync();
        }

        /// <summary>
        /// Invalidates the file cache to force re-indexing.
        /// </summary>
        public void RefreshIndex(string rootPath = null)
        {
            indexer.InvalidateCache(rootPath);
        }
    }

    /// <summary>
    /// Represents a search result with ranking information.
    /// </summary>
    public class SearchResult(FileEntry file, int historyScore, ImageMoniker moniker)
    {
        public string FileName { get; } = file.FileName;
        public string FullPath { get; } = file.FullPath;
        public string RelativePath { get; } = file.RelativePath;
        public string FileNameLower { get; } = file.FileNameLower;
        public int HistoryScore { get; } = historyScore;
        public ImageMoniker Moniker { get; } = moniker;
    }
}
