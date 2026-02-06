using Microsoft.VisualStudio.Imaging.Interop;

namespace InstaSearch.Services
{
    /// <summary>
    /// Represents a recently opened solution or folder from the VS MRU list.
    /// </summary>
    public class MruItem
    {
        public string FullPath { get; }
        public string DisplayName { get; }
        public MruItemKind Kind { get; }
        public ImageMoniker Moniker { get; }

        /// <summary>
        /// Lowercase display name for case-insensitive matching.
        /// </summary>
        public string DisplayNameLower { get; }

        public MruItem(string fullPath, string displayName, MruItemKind kind, ImageMoniker moniker)
        {
            FullPath = fullPath;
            DisplayName = displayName;
            Kind = kind;
            Moniker = moniker;
            DisplayNameLower = displayName.ToLowerInvariant();
        }
    }

    /// <summary>
    /// The kind of MRU entry.
    /// </summary>
    public enum MruItemKind
    {
        Solution,
        Folder
    }
}
