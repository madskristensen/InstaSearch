using System.Collections.Generic;
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
        // Note: The lambda defers reading options until indexing occurs, ensuring fresh values
        private static readonly FileIndexer _indexer = new(GetIgnoredFolders);
        private static readonly SearchHistoryService _history = new();
        private static readonly SearchService _searchService = new(_indexer, _history, GetIgnoredFilePatterns);
        private static readonly SearchRootResolver _rootResolver = new();
        private static readonly MruService _mruService = new();
        private static RatingPrompt _ratingPrompt;

        private static IgnoredFolderFilter GetIgnoredFolders() => General.Instance.GetIgnoredFolderFilter();
        private static IReadOnlyList<string> GetIgnoredFilePatterns() => General.Instance.GetIgnoredFilePatternsList();

        // Track the open dialog instance to prevent multiple windows
        private static SearchDialog _openDialog;

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Get the search root
            var rootPath = await _rootResolver.GetSearchRootAsync();
            if (string.IsNullOrEmpty(rootPath))
            {
                // No workspace open — show MRU search instead
                await ShowMruSearchDialogAsync();
                return;
            }

            // Set the workspace root for history (loads history for this workspace)
            _history.SetWorkspaceRoot(rootPath);

            // Get the image service for file icons
            IVsImageService2 imageService = await VS.GetServiceAsync<SVsImageService, IVsImageService2>();

            // If dialog is already open, just activate it
            if (_openDialog != null)
            {
                _openDialog.Activate();
                return;
            }

            // Get the main VS window for positioning
            Window mainWindow = Application.Current.MainWindow;

            // Create and show the search dialog
            var dialog = new SearchDialog(_searchService, imageService, rootPath);

            if (mainWindow != null)
            {
                dialog.Owner = mainWindow;
            }

            dialog.Topmost = true;
            dialog.FilesSelected += OnFilesSelected;
            dialog.Closed += (s, args) => _openDialog = null;
            _openDialog = dialog;

            dialog.ShowDialog();
        }

        /// <summary>
        /// Shows the MRU search dialog for recently opened solutions and folders.
        /// </summary>
        private static async Task ShowMruSearchDialogAsync()
        {
            IReadOnlyList<MruItem> mruItems = await _mruService.GetMruItemsAsync();
            if (mruItems.Count == 0)
            {
                await VS.StatusBar.ShowMessageAsync("No recent solutions or folders found.");
                return;
            }

            Window mainWindow = Application.Current.MainWindow;
            var dialog = new MruSearchDialog(mruItems);

            if (mainWindow != null)
            {
                dialog.Owner = mainWindow;
            }

            dialog.Topmost = true;

            if (dialog.ShowDialog() == true && dialog.SelectedItem != null)
            {
                MruItem selected = dialog.SelectedItem;
                OpenMruItemAsync(selected).FireAndForget();
            }
        }

        /// <summary>
        /// Opens the selected MRU item (solution or folder) in Visual Studio.
        /// </summary>
        private static async Task OpenMruItemAsync(MruItem item)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (item.Kind == MruItemKind.Folder)
                {
                    var solution = await VS.GetServiceAsync<SVsSolution, IVsSolution7>();
                    solution?.OpenFolder(item.FullPath);
                }
                else
                {
                    var dte = await VS.GetServiceAsync<EnvDTE.DTE, EnvDTE80.DTE2>();
                    dte?.Solution.Open(item.FullPath);
                }
            }
            catch (Exception ex)
            {
                await VS.StatusBar.ShowMessageAsync($"Error opening: {ex.Message}");
                await ex.LogAsync();
            }
        }

        private static void OnFilesSelected(object sender, FilesSelectedEventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.StartOnIdle(async () =>
            {
                try
                {
                    IReadOnlyList<SearchResult> selectedFiles = e.SelectedFiles;
                    var lineNumber = e.LineNumber;
                    var columnNumber = e.ColumnNumber;

                    // Open all selected files
                    DocumentView lastDocumentView = null;
                    foreach (SearchResult file in selectedFiles)
                    {
                        // Record the selection for history
                        await _searchService.RecordSelectionAsync(file.FullPath);

                        // Open the file in VS
                        lastDocumentView = await VS.Documents.OpenAsync(file.FullPath);
                    }

                    // Navigate to specific line and column in the last opened file (typically the first selected)
                    // Line/column number only applies when a single file is selected
                    if (lineNumber.HasValue && selectedFiles.Count == 1 && lastDocumentView?.TextView != null)
                    {
                        await NavigateToLineAsync(lastDocumentView.TextView, lineNumber.Value, columnNumber);
                    }

                    // Register successful usage for rating prompt
                    _ratingPrompt ??= new RatingPrompt("MadsKristensen.InstaSearch", Vsix.Name, await General.GetLiveInstanceAsync());
                    _ratingPrompt.RegisterSuccessfulUsage();
                }
                catch (Exception ex)
                {
                    await VS.StatusBar.ShowMessageAsync($"Error opening file: {ex.Message}");
                    await ex.LogAsync();
                }
            });
        }

        /// <summary>
        /// Navigates to a specific line and optional column number in the text view.
        /// </summary>
        /// <param name="textView">The text view to navigate in.</param>
        /// <param name="lineNumber">The 1-based line number to navigate to.</param>
        /// <param name="columnNumber">The 1-based column number to navigate to, or null for beginning of line.</param>
        private static async Task NavigateToLineAsync(IWpfTextView textView, int lineNumber, int? columnNumber = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                ITextSnapshot snapshot = textView.TextSnapshot;

                // Convert 1-based line number to 0-based index
                var lineIndex = lineNumber - 1;

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
                SnapshotPoint caretPosition = line.Start;

                // Calculate caret position: offset into the line by column if specified, start of line otherwise
                if (columnNumber.HasValue)
                {
                    caretPosition += Math.Max(0, Math.Min(columnNumber.Value - 1, line.Length));
                }

                // Move caret to the calculated position
                textView.Caret.MoveTo(caretPosition);

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
