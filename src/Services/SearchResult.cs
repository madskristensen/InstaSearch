using Microsoft.VisualStudio.Imaging.Interop;

namespace InstaSearch.Services
{
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
