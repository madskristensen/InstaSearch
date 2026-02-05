using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using InstaSearch.Options;
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

        // Regex to match :lineNumber at the end of the query (e.g., "file.cs:42")
        private static readonly Regex _lineNumberPattern = new(@":(\d+)$", RegexOptions.Compiled);

        private readonly SearchService _searchService;
        private readonly IVsImageService2 _imageService;
        private readonly string _rootPath;
        private readonly DispatcherTimer _debounceTimer;
        private CancellationTokenSource _searchCts;
        private List<SearchResult> _selectedResults = [];
        private string _pendingQuery;
        private int? _selectedLineNumber;
        private bool _isClosing;

        /// <summary>
        /// Raised when files are selected and should be opened.
        /// </summary>
        public event EventHandler<FilesSelectedEventArgs> FilesSelected;

        /// <summary>
        /// Gets the selected files. Use this for multi-select scenarios.
        /// </summary>
        public IReadOnlyList<SearchResult> SelectedFiles => _selectedResults;

        /// <summary>
        /// Gets the first selected file for backwards compatibility.
        /// </summary>
        public SearchResult SelectedFile => _selectedResults.Count > 0 ? _selectedResults[0] : null;

        /// <summary>
        /// Gets the line number to navigate to (1-based), or null if not specified.
        /// Only applies to the first selected file.
        /// </summary>
        public int? SelectedLineNumber => _selectedLineNumber;

        public SearchDialog(SearchService searchService, IVsImageService2 imageService, string rootPath)
        {
            InitializeComponent();
            _searchService = searchService;
            _imageService = imageService;
            _rootPath = rootPath;

            // Restore saved window size
            General settings = General.Instance;
            Width = settings.GetDialogWidth();
            Height = settings.GetDialogHeight();

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
            // Close when user clicks outside the dialog (but not if already closing)
            if (!_isClosing)
            {
                _isClosing = true;
                Close();
            }
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

        /// <summary>
        /// Parses the query to extract file search text and optional line number.
        /// </summary>
        /// <param name="query">The raw query (e.g., "file.cs:42")</param>
        /// <returns>Tuple of (searchQuery, lineNumber or null)</returns>
        private static (string searchQuery, int? lineNumber) ParseQueryWithLineNumber(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return (query, null);
            }

            Match match = _lineNumberPattern.Match(query);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var lineNumber) && lineNumber > 0)
            {
                // Remove the :lineNumber suffix from the search query
                var searchQuery = query.Substring(0, match.Index);
                return (searchQuery, lineNumber);
            }

            return (query, null);
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

                // Parse line number from query (e.g., "file.cs:42")
                (var searchQuery, var lineNumber) = ParseQueryWithLineNumber(query);
                _selectedLineNumber = lineNumber;

                IReadOnlyList<SearchResult> results = await _searchService.SearchAsync(_rootPath, searchQuery, _imageService, 100, token);

                if (!token.IsCancellationRequested)
                {
                    ResultsListBox.ItemsSource = results;

                    if (results.Count > 0)
                    {
                        ResultsListBox.SelectedIndex = 0;
                        ResultsListBox.ScrollIntoView(ResultsListBox.Items[0]);

                        var lineInfo = lineNumber.HasValue ? $" (line {lineNumber})" : "";
                        StatusText.Text = $"{results.Count} file{(results.Count == 1 ? "" : "s")} found{lineInfo}";
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
            var shiftHeld = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            switch (e.Key)
            {
                case Key.Escape:
                    _selectedResults.Clear();
                    _isClosing = true;
                    DialogResult = false;
                    Close();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        OpenFileWithoutClosing();
                    }
                    else
                    {
                        SelectCurrentItem();
                    }
                    e.Handled = true;
                    break;

                case Key.Down:
                    MoveSelection(1, shiftHeld);
                    e.Handled = true;
                    break;

                case Key.Up:
                    MoveSelection(-1, shiftHeld);
                    e.Handled = true;
                    break;

                case Key.PageDown:
                    MoveSelection(10, shiftHeld);
                    e.Handled = true;
                    break;

                case Key.PageUp:
                    MoveSelection(-10, shiftHeld);
                    e.Handled = true;
                    break;

                // Home and End keys are reserved for text navigation in the search box
                // and are not used for result list navigation
            }
        }

        // Track the anchor point for shift-selection and current focus position
        private int _selectionAnchor = -1;
        private int _focusIndex = 0;

        private void MoveSelection(int delta, bool extendSelection)
        {
            if (ResultsListBox.Items.Count == 0)
                return;

            // Use focus index for calculating new position (not SelectedIndex which returns first selected item)
            if (_focusIndex < 0 || _focusIndex >= ResultsListBox.Items.Count)
            {
                _focusIndex = ResultsListBox.SelectedIndex >= 0 ? ResultsListBox.SelectedIndex : 0;
            }

            var newIndex = _focusIndex + delta;
            newIndex = Math.Max(0, Math.Min(newIndex, ResultsListBox.Items.Count - 1));

            if (extendSelection)
            {
                ExtendSelectionTo(newIndex);
            }
            else
            {
                // Reset anchor when not extending
                _selectionAnchor = newIndex;
                ResultsListBox.SelectedIndex = newIndex;
            }

            // Update focus index to track where we are
            _focusIndex = newIndex;
            ResultsListBox.ScrollIntoView(ResultsListBox.Items[newIndex]);
        }

        /// <summary>
        /// Extends the selection from the anchor point to the target index.
        /// </summary>
        private void ExtendSelectionTo(int targetIndex)
        {
            if (ResultsListBox.Items.Count == 0)
                return;

            // Initialize anchor if not set
            if (_selectionAnchor < 0 || _selectionAnchor >= ResultsListBox.Items.Count)
            {
                _selectionAnchor = ResultsListBox.SelectedIndex >= 0 ? ResultsListBox.SelectedIndex : 0;
            }

            // Update focus index
            _focusIndex = targetIndex;

            // Calculate range
            var start = Math.Min(_selectionAnchor, targetIndex);
            var end = Math.Max(_selectionAnchor, targetIndex);

            // Clear and select range
            ResultsListBox.SelectedItems.Clear();
            for (var i = start; i <= end; i++)
            {
                ResultsListBox.SelectedItems.Add(ResultsListBox.Items[i]);
            }
        }

        private void SelectCurrentItem()
        {
            // Get all selected items (supports multi-select with Ctrl+Click or Shift+Click)
            _selectedResults = ResultsListBox.SelectedItems
                .Cast<SearchResult>()
                .ToList();

            if (_selectedResults.Count > 0)
            {
                _isClosing = true;
                FilesSelected?.Invoke(this, new FilesSelectedEventArgs(_selectedResults, _selectedLineNumber));
                Close();
            }
        }

        private async void OpenFileWithoutClosing()
        {
            if (ResultsListBox.SelectedItem is SearchResult result)
            {
                try
                {
                    await VS.Documents.OpenAsync(result.FullPath);
                    await _searchService.RecordSelectionAsync(result.FullPath);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Error opening file: {ex.Message}";
                }
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

        private async void RefreshLink_Click(object sender, RoutedEventArgs e)
        {
            // Invalidate cache and re-search
            _searchService.RefreshIndex(_rootPath);
            await PerformSearchAsync(SearchTextBox.Text);
            SearchTextBox.Focus();
        }

        protected override void OnClosed(EventArgs e)
        {
            _debounceTimer.Stop();
            _searchCts?.Cancel();
            _searchCts?.Dispose();

            // Save window size for next time
            General settings = General.Instance;
            settings.DialogWidth = Width;
            settings.DialogHeight = Height;
            settings.Save();

            base.OnClosed(e);
        }
    }
}
