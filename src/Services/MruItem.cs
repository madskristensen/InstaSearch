using Microsoft.VisualStudio.Imaging.Interop;

namespace InstaSearch.Services
{
    /// <summary>
    /// Represents a recently opened solution, project, or folder from MRU sources.
    /// </summary>
    public class MruItem(string fullPath, string displayName, MruItemKind kind, ImageMoniker moniker)
    {
        public string FullPath { get; } = fullPath;
        public string DisplayName { get; } = displayName;
        public MruItemKind Kind { get; } = kind;
        public ImageMoniker Moniker { get; } = moniker;

        /// <summary>
        /// Lowercase display name for case-insensitive matching.
        /// </summary>
        public string DisplayNameLower { get; } = displayName.ToLowerInvariant();
    }

    /// <summary>
    /// The kind of MRU entry.
    /// </summary>
    public enum MruItemKind
    {
        Solution,
        Project,
        Folder
    }
}
