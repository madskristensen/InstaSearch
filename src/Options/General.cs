using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace InstaSearch.Options
{
    /// <summary>
    /// General options for InstaSearch. Also implements IRatingConfig for rating prompt support.
    /// </summary>
    [ComVisible(true)]
    internal class General : BaseOptionModel<General>, IRatingConfig
    {
        private const double _defaultWidth = 600;
        private const double _defaultHeight = 400;
        private const double _minWidth = 400;
        private const double _minHeight = 250;
        private const double _maxWidth = 1200;
        private const double _maxHeight = 800;

        /// <summary>
        /// Default folders to ignore during file indexing.
        /// </summary>
        private const string _defaultIgnoredFolders = ".git, .vs, .idea, bin, obj, node_modules, packages, .nuget, TestResults, Debug, Release, .svn, .hg";

        [Category("Search")]
        [DisplayName("Ignored Folders")]
        [Description("Comma-separated list of folder names to exclude from search results. Changes take effect on next search.")]
        [DefaultValue(_defaultIgnoredFolders)]
        public string IgnoredFolders { get; set; } = _defaultIgnoredFolders;

        [Browsable(false)]
        public int RatingRequests { get; set; }

        [Browsable(false)]
        public double DialogWidth { get; set; } = _defaultWidth;

        [Browsable(false)]
        public double DialogHeight { get; set; } = _defaultHeight;

        /// <summary>
        /// Gets the dialog width, clamped to valid range.
        /// </summary>
        public double GetDialogWidth() => Math.Max(_minWidth, Math.Min(_maxWidth, DialogWidth));

        /// <summary>
        /// Gets the dialog height, clamped to valid range.
        /// </summary>
        public double GetDialogHeight() => Math.Max(_minHeight, Math.Min(_maxHeight, DialogHeight));

        /// <summary>
        /// Parses the IgnoredFolders string into a HashSet for efficient lookup.
        /// </summary>
        public HashSet<string> GetIgnoredFoldersSet()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(IgnoredFolders))
            {
                return folders;
            }

            foreach (var folder in IgnoredFolders.Split(','))
            {
                var trimmed = folder.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    folders.Add(trimmed);
                }
            }

            return folders;
        }
    }
}
