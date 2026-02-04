using System.Windows;
using InstaSearch.Services;
using InstaSearch.UI;
using Microsoft.VisualStudio.Shell.Interop;

namespace InstaSearch
{
    [Command(PackageIds.SearchCommand)]
    internal sealed class SearchCommand : BaseCommand<SearchCommand>
    {
        // Shared services for performance (reuse across invocations)
        private static readonly FileIndexer _indexer = new();
        private static readonly SearchHistoryService _history = new();
        private static readonly SearchService _searchService = new(_indexer, _history);
        private static readonly SearchRootResolver _rootResolver = new();

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

            if (result == true && dialog.SelectedFile != null)
            {
                var filePath = dialog.SelectedFile.FullPath;

                // Record the selection for history
                await _searchService.RecordSelectionAsync(filePath);

                // Open the file in VS
                await VS.Documents.OpenAsync(filePath);
            }
        }
    }
}
