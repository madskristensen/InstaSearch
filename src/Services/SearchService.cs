using System.Collections.Generic;
using System.IO;
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
    public class SearchService(FileIndexer indexer, SearchHistoryService history, Func<IReadOnlyList<string>> getIgnoredFilePatterns = null)
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
                // Use heap-based selection for O(n log k) instead of O(n log n) full sort
                rankedFiles = SelectTopN(
                    files.Where(f => history.GetSelectionCount(f.FullPath) > 0 && !IsExcludedByPattern(f.FileNameLower)),
                    f => new RankedFile(f, history.GetSelectionCount(f.FullPath), false, IsCodeFile(f.FileName)),
                    maxResults);

                // Empty query - no highlighting needed
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                var historyResults = new List<SearchResult>(rankedFiles.Count);
                foreach (FileEntry f in rankedFiles)
                {
                    historyResults.Add(new SearchResult(f, history.GetSelectionCount(f.FullPath), GetMoniker(imageService, f.FileName), string.Empty));
                }
                return historyResults;
            }

            var queryLower = query.ToLowerInvariant();

            // Check if query contains wildcards
            var hasWildcard = queryLower.Contains('*');

            if (hasWildcard)
            {
                // Pre-parse wildcard pattern once - avoids allocations during matching
                var wildcardPattern = new WildcardPattern(queryLower);

                // Use heap-based selection for O(n log k) instead of O(n log n) full sort
                rankedFiles = SelectTopN(
                    files.Where(f => wildcardPattern.Matches(f.FileNameLower) && !IsExcludedByPattern(f.FileNameLower)),
                    f => new RankedFile(f, history.GetSelectionCount(f.FullPath), wildcardPattern.StartsWithFirstSegment(f.FileNameLower), IsCodeFile(f.FileName)),
                    maxResults);
            }
            else
            {
                // Use heap-based selection for O(n log k) instead of O(n log n) full sort
                rankedFiles = SelectTopN(
                    files.Where(f => f.FileNameLower.IndexOf(queryLower, StringComparison.Ordinal) >= 0 && !IsExcludedByPattern(f.FileNameLower)),
                    f => new RankedFile(f, history.GetSelectionCount(f.FullPath), f.FileNameLower.StartsWith(queryLower, StringComparison.Ordinal), IsCodeFile(f.FileName)),
                    maxResults);
            }

            // Only now switch to UI thread to get monikers - only for final results
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Create results with monikers - only for the limited result set
            var results = new List<SearchResult>(rankedFiles.Count);
            foreach (FileEntry f in rankedFiles)
            {
                results.Add(new SearchResult(f, history.GetSelectionCount(f.FullPath), GetMoniker(imageService, f.FileName), queryLower));
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
        /// File extensions that are typically not edited in VS (binary, media, etc.).
        /// These will be deprioritized in search results.
        /// </summary>
        private static readonly HashSet<string> _binaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp", ".tiff", ".tif",
            // Video/Audio
            ".mp4", ".avi", ".mov", ".wmv", ".mp3", ".wav", ".ogg", ".flac",
            // Executables/Binaries
            ".exe", ".dll", ".pdb", ".obj", ".lib", ".so", ".dylib",
            // Archives
            ".zip", ".7z", ".rar", ".tar", ".gz", ".nupkg",
            // Fonts
            ".ttf", ".otf", ".woff", ".woff2", ".eot",
            // Documents (typically not edited in VS)
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            // Other binary
            ".bin", ".dat", ".db", ".sqlite", ".mdb"
        };

        /// <summary>
        /// Ranked file entry for sorting. Using struct to avoid allocations.
        /// </summary>
        private readonly struct RankedFile(FileEntry file, int score, bool startsWithQuery, bool isCodeFile) : IComparable<RankedFile>
        {
            public readonly FileEntry File = file;
            public readonly int Score = score;
            public readonly bool StartsWithQuery = startsWithQuery;
            public readonly bool IsCodeFile = isCodeFile;

            /// <summary>
            /// Compare for descending score, descending isCodeFile, descending startsWithQuery, ascending filename.
            /// Returns negative if this should come BEFORE other in sorted order.
            /// </summary>
            public int CompareTo(RankedFile other)
            {
                // Higher score first (descending)
                var scoreCompare = other.Score.CompareTo(Score);
                if (scoreCompare != 0) return scoreCompare;

                // Code files first (descending - true > false)
                var codeCompare = IsCodeFile.CompareTo(other.IsCodeFile);
                if (codeCompare != 0) return -codeCompare;

                // StartsWithQuery=true first (descending)
                var startsCompare = other.StartsWithQuery.CompareTo(StartsWithQuery);
                if (startsCompare != 0) return startsCompare;

                // Alphabetical by filename (ascending)
                return string.Compare(File.FileName, other.File.FileName, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Checks if a file is a code/text file (not a binary file like images, executables, etc.).
        /// </summary>
        private static bool IsCodeFile(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            return !_binaryExtensions.Contains(extension);
        }

        /// <summary>
        /// Checks if a file should be excluded based on user-configured ignored file patterns.
        /// Patterns support simple wildcards (e.g., *.designer.cs).
        /// </summary>
        private bool IsExcludedByPattern(string fileNameLower)
        {
            IReadOnlyList<string> patterns = getIgnoredFilePatterns?.Invoke();
            if (patterns == null || patterns.Count == 0)
            {
                return false;
            }

            foreach (var pattern in patterns)
            {
                if (MatchesSimplePattern(fileNameLower, pattern))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Matches a filename against a simple pattern with leading wildcard (e.g., *.designer.cs).
        /// Supports patterns like: *.ext, *.suffix.ext, exact.name
        /// </summary>
        private static bool MatchesSimplePattern(string fileNameLower, string pattern)
        {
            if (pattern.Length == 0)
            {
                return false;
            }

            if (pattern[0] == '*')
            {
                // *.designer.cs -> check if filename ends with ".designer.cs"
                var suffix = pattern.Substring(1);
                return fileNameLower.EndsWith(suffix, StringComparison.Ordinal);
            }

            // Exact match
            return fileNameLower.Equals(pattern, StringComparison.Ordinal);
        }

        /// <summary>
        /// Selects top N items using a simple list-based approach optimized for small k.
        /// Maintains a sorted list of the k best items seen so far.
        /// O(n*k) but with very low constant factors for typical maxResults (100).
        /// </summary>
        private static List<FileEntry> SelectTopN(IEnumerable<FileEntry> source, Func<FileEntry, RankedFile> selector, int maxResults)
        {
            var topItems = new List<RankedFile>(maxResults + 1);

            foreach (FileEntry file in source)
            {
                RankedFile ranked = selector(file);

                // Binary search to find insertion point
                var insertIndex = topItems.BinarySearch(ranked);
                if (insertIndex < 0) insertIndex = ~insertIndex;

                // Only insert if it would be in top k
                if (insertIndex < maxResults)
                {
                    topItems.Insert(insertIndex, ranked);

                    // Remove worst item if over capacity
                    if (topItems.Count > maxResults)
                    {
                        topItems.RemoveAt(maxResults);
                    }
                }
            }

            return topItems.ConvertAll(r => r.File);
        }

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
    public class SearchResult(FileEntry file, int historyScore, ImageMoniker moniker, string queryLower)
    {
        public string FileName { get; } = file.FileName;
        public string FullPath { get; } = file.FullPath;
        public string RelativePath { get; } = file.RelativePath;
        public string FileNameLower { get; } = file.FileNameLower;
        public int HistoryScore { get; } = historyScore;
        public ImageMoniker Moniker { get; } = moniker;

        /// <summary>
        /// The search query (pre-lowercased for efficient highlighting).
        /// </summary>
        public string QueryLower { get; } = queryLower ?? string.Empty;
    }
}
