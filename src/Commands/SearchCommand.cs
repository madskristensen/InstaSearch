using System.Windows;
using InstaSearch.Options;
using InstaSearch.Services;
using InstaSearch.UI;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace InstaSearch
{
    [Command(PackageIds.SearchCommand)]
    internal sealed class SearchCommand : BaseCommand<SearchCommand>
    {
        // Shared services for performance (reuse across invocations)
        private static readonly FileIndexer _indexer = new(() => General.Instance.GetIgnoredFoldersSet());
        private static readonly SearchHistoryService _history = new();
        private static readonly SearchService _searchService = new(_indexer, _history);
        private static readonly SearchRootResolver _rootResolver = new();
        private static RatingPrompt _ratingPrompt;

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Get the search root
            var rootPath = await _rootResolver.GetSearchRootAsync();
            if (string.IsNullOrEmpty(rootPath))
            {
                await VS.StatusBar.ShowMessageAsync("No solution, folder, or repository is open. Please open a solution or folder first.");
                return;
            }

            // Set the workspace root for history (loads history for this workspace)
            _history.SetWorkspaceRoot(rootPath);

            // Get the image service for file icons
            IVsImageService2 imageService = await VS.GetServiceAsync<SVsImageService, IVsImageService2>();

            // Get the main VS window for positioning
            Window mainWindow = Application.Current.MainWindow;

            // Create and show the search dialog
            var dialog = new SearchDialog(_searchService, imageService, rootPath);

            if (mainWindow != null)
            {
                dialog.Owner = mainWindow;
            }

            var result = dialog.ShowDialog();

            if (result == true && dialog.SelectedFiles.Count > 0)
            {
                var selectedFiles = dialog.SelectedFiles;
                var lineNumber = dialog.SelectedLineNumber;

                // Open all selected files
                DocumentView lastDocumentView = null;
                foreach (var file in selectedFiles)
                {
                    // Record the selection for history
                    await _searchService.RecordSelectionAsync(file.FullPath);

                    // Open the file in VS
                    lastDocumentView = await VS.Documents.OpenAsync(file.FullPath);
                }

                // Navigate to specific line in the last opened file (typically the first selected)
                // Line number only applies when a single file is selected
                if (lineNumber.HasValue && selectedFiles.Count == 1 && lastDocumentView?.TextView != null)
                {
                    await NavigateToLineAsync(lastDocumentView.TextView, lineNumber.Value);
                }

                // Register successful usage for rating prompt
                _ratingPrompt ??= new RatingPrompt("MadsKristensen.InstaSearch", Vsix.Name, await General.GetLiveInstanceAsync());
                _ratingPrompt.RegisterSuccessfulUsage();
            }
        }

        /// <summary>
        /// Navigates to a specific line number in the text view.
        /// </summary>
        /// <param name="textView">The text view to navigate in.</param>
        /// <param name="lineNumber">The 1-based line number to navigate to.</param>
        private static async Task NavigateToLineAsync(IWpfTextView textView, int lineNumber)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                ITextSnapshot snapshot = textView.TextSnapshot;

                // Convert 1-based line number to 0-based index
                int lineIndex = lineNumber - 1;

                // Clamp to valid range
                if (lineIndex < 0)
                {
                    lineIndex = 0;
                }
                else if (lineIndex >= snapshot.LineCount)
                {
                    lineIndex = snapshot.LineCount - 1;
                }

                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineIndex);

                // Move caret to the beginning of the line
                textView.Caret.MoveTo(line.Start);

                // Center the line in the view
                textView.ViewScroller.EnsureSpanVisible(
                    new SnapshotSpan(line.Start, line.End),
                    EnsureSpanVisibleOptions.AlwaysCenter);
            }
            catch (Exception)
            {
                // Silently fail if navigation fails - the file is still open
            }
        }
    }
}
