using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using InstaSearch.Services;
using Microsoft.VisualStudio.Shell.Interop;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using TextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;

namespace InstaSearch.UI
{
    /// <summary>
    /// Fast file search dialog with real-time results.
    /// </summary>
    public partial class SearchDialog : Window
    {
        private const int _debounceDelayMs = 150;

        private readonly SearchService _searchService;
        private readonly IVsImageService2 _imageService;
        private readonly string _rootPath;
        private readonly DispatcherTimer _debounceTimer;
        private CancellationTokenSource _searchCts;
        private SearchResult _selectedResult;
        private string _pendingQuery;

        public SearchResult SelectedFile => _selectedResult;

        public SearchDialog(SearchService searchService, IVsImageService2 imageService, string rootPath)
        {
            InitializeComponent();
            _searchService = searchService;
            _imageService = imageService;
            _rootPath = rootPath;

            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_debounceDelayMs)
            };
            _debounceTimer.Tick += DebounceTimer_Tick;

            Loaded += SearchDialog_Loaded;
            Deactivated += SearchDialog_Deactivated;
        }

        private void SearchDialog_Deactivated(object sender, EventArgs e)
        {
            // Close when clicking outside the dialog
            _selectedResult = null;
            DialogResult = false;
            Close();
        }

        private async void SearchDialog_Loaded(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Focus();

            // Trigger initial search to show history items
            await PerformSearchAsync(string.Empty);
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Debounce: restart timer on each keystroke
            _pendingQuery = SearchTextBox.Text;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private async void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            await PerformSearchAsync(_pendingQuery);
        }

        private async Task PerformSearchAsync(string query)
        {
            // Cancel any pending search
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            CancellationToken token = _searchCts.Token;

            try
            {
                StatusText.Text = "Searching...";

                IReadOnlyList<SearchResult> results = await _searchService.SearchAsync(_rootPath, query, _imageService, 100, token);

                if (!token.IsCancellationRequested)
                {
                    ResultsListBox.ItemsSource = results;

                    if (results.Count > 0)
                    {
                        ResultsListBox.SelectedIndex = 0;
                        ResultsListBox.ScrollIntoView(ResultsListBox.Items[0]);
                        StatusText.Text = $"{results.Count} file{(results.Count == 1 ? "" : "s")} found";
                    }
                    else
                    {
                        StatusText.Text = string.IsNullOrEmpty(query)
                            ? "Type to search files"
                            : "No files found";
                    }
                }
            }
            catch (System.OperationCanceledException)
            {
                // Search was cancelled, ignore
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    _selectedResult = null;
                    DialogResult = false;
                    Close();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    SelectCurrentItem();
                    e.Handled = true;
                    break;

                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;

                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;

                case Key.PageDown:
                    MoveSelection(10);
                    e.Handled = true;
                    break;

                case Key.PageUp:
                    MoveSelection(-10);
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int delta)
        {
            if (ResultsListBox.Items.Count == 0)
                return;

            var newIndex = ResultsListBox.SelectedIndex + delta;
            newIndex = Math.Max(0, Math.Min(newIndex, ResultsListBox.Items.Count - 1));

            ResultsListBox.SelectedIndex = newIndex;
            ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
        }

        private void SelectCurrentItem()
        {
            if (ResultsListBox.SelectedItem is SearchResult result)
            {
                _selectedResult = result;
                DialogResult = true;
                Close();
            }
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectCurrentItem();
        }

        private void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Keep focus on search box while allowing list selection
            SearchTextBox.Focus();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _debounceTimer.Stop();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            base.OnClosed(e);
        }
    }
}
