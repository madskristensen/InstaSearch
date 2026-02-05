using System.Collections.Generic;

namespace InstaSearch.Services
{
    /// <summary>
    /// Pre-parsed search query with optional extension and path filters.
    /// Parsed once per keystroke, then applied per file with zero allocations.
    /// </summary>
    /// <remarks>
    /// Syntax examples:
    ///   dialog .cs .ts      → substring "dialog", only .cs and .ts files
    ///   dialog -.xaml -.resx → substring "dialog", exclude .xaml and .resx files
    ///   dialog \src\         → substring "dialog", only files under a "src" folder
    ///   dialog \src\ .cs     → all three combined
    /// </remarks>
    internal readonly struct SearchQuery
    {
        /// <summary>The core search text (lowercased), with modifiers stripped.</summary>
        public readonly string Text;

        /// <summary>Extension include filters (e.g. ".cs", ".ts"). Empty means no filter.</summary>
        public readonly string[] IncludeExtensions;

        /// <summary>Extension exclude filters (e.g. ".xaml"). Empty means no filter.</summary>
        public readonly string[] ExcludeExtensions;

        /// <summary>Path segment filters (e.g. "\src\"). Empty means no filter.</summary>
        public readonly string[] PathFilters;

        /// <summary>Whether the core text contains wildcards.</summary>
        public readonly bool HasWildcard;

        /// <summary>Whether any filters are active.</summary>
        public readonly bool HasFilters;

        private SearchQuery(string text, string[] includeExtensions, string[] excludeExtensions, string[] pathFilters)
        {
            Text = text;
            IncludeExtensions = includeExtensions;
            ExcludeExtensions = excludeExtensions;
            PathFilters = pathFilters;
            HasWildcard = text.Contains("*");
            HasFilters = includeExtensions.Length > 0 || excludeExtensions.Length > 0 || pathFilters.Length > 0;
        }

        /// <summary>
        /// Parses a raw query string into a SearchQuery. O(n) on query length, no regex.
        /// </summary>
        public static SearchQuery Parse(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new SearchQuery(string.Empty, [], [], []);
            }

            var queryLower = query.ToLowerInvariant();

            // Fast path: no spaces means no modifiers possible — skip split entirely
            if (queryLower.IndexOf(' ') < 0)
            {
                return new SearchQuery(queryLower, [], [], []);
            }

            // Split by spaces to find modifiers
            var parts = queryLower.Split(' ');

            List<string> textParts = null;
            List<string> includeExt = null;
            List<string> excludeExt = null;
            List<string> pathFilters = null;

            foreach (var part in parts)
            {
                if (part.Length == 0)
                {
                    continue;
                }

                // Exclude extension: starts with "-." (e.g., "-.xaml")
                if (part.Length >= 2 && part[0] == '-' && part[1] == '.')
                {
                    excludeExt ??= [];
                    excludeExt.Add(part.Substring(1)); // store as ".xaml"
                }
                // Path filter: starts and ends with \ (e.g., "\src\")
                else if (part.Length >= 3 && part[0] == '\\' && part[part.Length - 1] == '\\')
                {
                    pathFilters ??= [];
                    // Store with separators for IndexOf matching (e.g., "\src\")
                    pathFilters.Add(part);
                }
                // Include extension: starts with "." and has no wildcards (e.g., ".cs")
                else if (part[0] == '.' && !part.Contains("*"))
                {
                    includeExt ??= [];
                    includeExt.Add(part);
                }
                else
                {
                    // Regular search text
                    textParts ??= [];
                    textParts.Add(part);
                }
            }

            var text = textParts != null ? string.Join(" ", textParts) : string.Empty;

            return new SearchQuery(
                text,
                includeExt?.ToArray() ?? [],
                excludeExt?.ToArray() ?? [],
                pathFilters?.ToArray() ?? []);
        }

        /// <summary>
        /// Checks if a file passes all active filters. Zero allocations.
        /// </summary>
        /// <param name="fileNameLower">Lowercased file name.</param>
        /// <param name="relativePathLower">Lowercased relative path (with backslash separators).</param>
        public bool PassesFilters(string fileNameLower, string relativePathLower)
        {
            if (!HasFilters)
            {
                return true;
            }

            // Check extension includes: file must end with at least one
            if (IncludeExtensions.Length > 0)
            {
                var matched = false;
                foreach (var ext in IncludeExtensions)
                {
                    if (fileNameLower.EndsWith(ext, System.StringComparison.Ordinal))
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    return false;
                }
            }

            // Check extension excludes: file must not end with any
            foreach (var ext in ExcludeExtensions)
            {
                if (fileNameLower.EndsWith(ext, System.StringComparison.Ordinal))
                {
                    return false;
                }
            }

            // Check path filters: relative path must contain each segment
            foreach (var pathSegment in PathFilters)
            {
                if (relativePathLower.IndexOf(pathSegment, System.StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
