using System.Collections.Generic;
using InstaSearch.Services;

namespace InstaSearch.UI
{
    /// <summary>
    /// Event args for when files are selected in the search dialog.
    /// </summary>
    public class FilesSelectedEventArgs(IReadOnlyList<SearchResult> selectedFiles, int? lineNumber) : EventArgs
    {
        public IReadOnlyList<SearchResult> SelectedFiles { get; } = selectedFiles;
        public int? LineNumber { get; } = lineNumber;
    }
}
