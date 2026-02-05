using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using InstaSearch.Services;

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
        [Description("Comma-separated list of folder names to exclude from search results. Supports wildcards (e.g., *.Migrations). Changes take effect on next search.")]
        [DefaultValue(_defaultIgnoredFolders)]
        public string IgnoredFolders { get; set; } = _defaultIgnoredFolders;

        /// <summary>
        /// Default file patterns to exclude from search results.
        /// </summary>
        private const string _defaultIgnoredFilePatterns = "*.designer.cs, *.g.cs, *.g.i.cs, *.generated.cs, *.AssemblyInfo.cs";

        [Category("Search")]
        [DisplayName("Ignored File Patterns")]
        [Description("Comma-separated list of file name patterns to exclude from search results (e.g., *.designer.cs, *.g.cs). Supports wildcards (*). Changes take effect on next search.")]
        [DefaultValue(_defaultIgnoredFilePatterns)]
        public string IgnoredFilePatterns { get; set; } = _defaultIgnoredFilePatterns;

        [Category("Search")]
        [DisplayName("Take over Go To All")]
        [Description("When true, this setting will take over Ctrl+T and Ctrl+P for the built in Go To All command.")]
        [DefaultValue(true)]
        public bool TakeOverGoToAll { get; set; } = true;


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
        /// Parses the IgnoredFolders string into an IgnoredFolderFilter with exact names and wildcard patterns.
        /// </summary>
        public IgnoredFolderFilter GetIgnoredFolderFilter()
        {
            var exactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var wildcardPatterns = new List<string>();

            if (string.IsNullOrWhiteSpace(IgnoredFolders))
            {
                return new IgnoredFolderFilter(exactNames, wildcardPatterns);
            }

            foreach (var folder in IgnoredFolders.Split(','))
            {
                var trimmed = folder.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    if (trimmed.Contains("*"))
                    {
                        wildcardPatterns.Add(trimmed);
                    }
                    else
                    {
                        exactNames.Add(trimmed);
                    }
                }
            }

            return new IgnoredFolderFilter(exactNames, wildcardPatterns);
        }
            /// <summary>
            /// Parses the IgnoredFilePatterns string into a list of lowercase patterns.
            /// </summary>
            public IReadOnlyList<string> GetIgnoredFilePatternsList()
            {
                var patterns = new List<string>();

                if (string.IsNullOrWhiteSpace(IgnoredFilePatterns))
                {
                    return patterns;
                }

                foreach (var pattern in IgnoredFilePatterns.Split(','))
                {
                    var trimmed = pattern.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        patterns.Add(trimmed.ToLowerInvariant());
                    }
                }

                return patterns;
            }
        }
    }
