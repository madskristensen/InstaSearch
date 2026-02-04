using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace InstaSearch.Services
{
    /// <summary>
    /// Fast file search service with history-based ranking.
    /// </summary>
    public class SearchService
    {
        private readonly FileIndexer _indexer;
        private readonly SearchHistoryService _history;

        public SearchService(FileIndexer indexer, SearchHistoryService history)
        {
            _indexer = indexer;
            _history = history;
        }

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
                return Array.Empty<SearchResult>();
            }

            // Get indexed files
            IReadOnlyList<FileEntry> files = await _indexer.IndexAsync(rootPath, cancellationToken);

            // Switch to UI thread to get monikers
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(query))
            {
                // Show most recently selected files when query is empty
                return files
                    .Select(f => new SearchResult(f, _history.GetSelectionCount(f.FullPath), GetMoniker(imageService, f.FileName)))
                    .Where(r => r.HistoryScore > 0)
                    .OrderByDescending(r => r.HistoryScore)
                    .ThenBy(r => r.FileName)
                    .Take(maxResults)
                    .ToList();
            }

            var queryLower = query.ToLowerInvariant();

            // Filter matches (can be done off thread)
            var matchedFiles = files
                .Where(f => f.FileNameLower.Contains(queryLower))
                .ToList();

            // Create results with monikers on UI thread
            var matches = matchedFiles
                .Select(f => new SearchResult(f, _history.GetSelectionCount(f.FullPath), GetMoniker(imageService, f.FileName)))
                .ToList();

            // Rank results: history score (descending), then exact start match, then alphabetical
            return matches
                .OrderByDescending(r => r.HistoryScore)
                .ThenByDescending(r => r.FileNameLower.StartsWith(queryLower))
                .ThenBy(r => r.FileName)
                .Take(maxResults)
                .ToList();
        }

        private static ImageMoniker GetMoniker(IVsImageService2 imageService, string fileName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return imageService.GetImageMonikerForFile(fileName);
        }

        /// <summary>
        /// Records that a file was selected and saves history.
        /// </summary>
        public async Task RecordSelectionAsync(string fullPath)
        {
            _history.RecordSelection(fullPath);
            await _history.SaveAsync();
        }

        /// <summary>
        /// Invalidates the file cache to force re-indexing.
        /// </summary>
        public void RefreshIndex(string rootPath = null)
        {
            _indexer.InvalidateCache(rootPath);
        }
    }

    /// <summary>
    /// Represents a search result with ranking information.
    /// </summary>
    public class SearchResult
    {
        public SearchResult(FileEntry file, int historyScore, ImageMoniker moniker)
        {
            FileName = file.FileName;
            FullPath = file.FullPath;
            RelativePath = file.RelativePath;
            FileNameLower = file.FileNameLower;
            HistoryScore = historyScore;
            Moniker = moniker;
        }

        public string FileName { get; }
        public string FullPath { get; }
        public string RelativePath { get; }
        public string FileNameLower { get; }
        public int HistoryScore { get; }
        public ImageMoniker Moniker { get; }
    }
}
