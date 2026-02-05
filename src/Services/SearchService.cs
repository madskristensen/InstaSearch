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

                // Filter, rank, and take BEFORE getting monikers
                rankedFiles = [.. files
                    .Where(f => hasWildcard
                        ? MatchesWildcard(f.FileNameLower, queryLower)
                        : f.FileNameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0)
                    .Select(f => (File: f, Score: history.GetSelectionCount(f.FullPath), StartsWithQuery: StartsWithPattern(f.FileNameLower, queryLower)))
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.StartsWithQuery)
                    .ThenBy(x => x.File.FileName)
                    .Take(maxResults)
                    .Select(x => x.File)];
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

        /// <summary>
        /// Fast wildcard matching without regex. Splits pattern by '*' and checks segments exist in order.
        /// Example: "test*.cs" splits to ["test", ".cs"], then verifies both exist in sequence.
        /// </summary>
        private static bool MatchesWildcard(string fileName, string pattern)
        {
            var segments = pattern.Split(_wildcardSeparator, StringSplitOptions.None);
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

        private static readonly char[] _wildcardSeparator = ['*'];

        /// <summary>
        /// Checks if filename starts with the query pattern (for ranking).
        /// </summary>
        private static bool StartsWithPattern(string fileName, string query)
        {
            if (query.Contains('*'))
            {
                // For wildcard queries, check if first segment matches at start
                var starIndex = query.IndexOf('*');
                return starIndex == 0 || fileName.StartsWith(query.Substring(0, starIndex), StringComparison.Ordinal);
            }
            return fileName.StartsWith(query, StringComparison.Ordinal);
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
