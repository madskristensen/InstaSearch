using System.IO;

namespace InstaSearch.Services
{
    /// <summary>
    /// Represents an indexed file entry.
    /// </summary>
    public class FileEntry(string fileName, string fullPath, string relativePath)
    {
        private string _fileNameLower;
        private string _relativePathLower;
        private string _extension;

        public string FileName { get; } = fileName;
        public string FullPath { get; } = fullPath;
        public string RelativePath { get; } = relativePath;

        /// <summary>
        /// Lazy-evaluated lowercase filename to avoid allocations when not needed.
        /// </summary>
        public string FileNameLower => _fileNameLower ??= FileName.ToLowerInvariant();

        /// <summary>
        /// Lazy-evaluated lowercase relative path for path filter matching.
        /// </summary>
        public string RelativePathLower => _relativePathLower ??= RelativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .ToLowerInvariant();

        /// <summary>
        /// Lazy-evaluated file extension to avoid repeated Path.GetExtension calls in ranking.
        /// </summary>
        public string Extension => _extension ??= Path.GetExtension(FileName);
    }
}
