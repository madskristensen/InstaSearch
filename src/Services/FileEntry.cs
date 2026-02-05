namespace InstaSearch.Services
{
    /// <summary>
    /// Represents an indexed file entry.
    /// </summary>
    public class FileEntry(string fileName, string fullPath, string relativePath)
    {
        private string _fileNameLower;

        public string FileName { get; } = fileName;
        public string FullPath { get; } = fullPath;
        public string RelativePath { get; } = relativePath;

        /// <summary>
        /// Lazy-evaluated lowercase filename to avoid allocations when not needed.
        /// </summary>
        public string FileNameLower => _fileNameLower ??= FileName.ToLowerInvariant();
    }
}
