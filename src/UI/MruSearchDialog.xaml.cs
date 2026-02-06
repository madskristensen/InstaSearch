using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using InstaSearch.Services;
using TextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;

namespace InstaSearch.UI
{
    /// <summary>
    /// Search dialog for recently opened solutions and folders.
    /// Shown when no workspace is currently open.
    /// </summary>
    public partial class MruSearchDialog : Window
    {
        private readonly IReadOnlyList<MruItem> _allItems;
        private bool _isClosing;

        /// <summary>
        /// Gets the selected MRU item, or null if cancelled.
        /// </summary>
        public MruItem SelectedItem { get; private set; }

        public MruSearchDialog(IReadOnlyList<MruItem> items)
        {
            InitializeComponent();
            _allItems = items;

            Loaded += MruSearchDialog_Loaded;
            Deactivated += MruSearchDialog_Deactivated;
        }

        private void MruSearchDialog_Loaded(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Focus();
            FilterResults(string.Empty);
        }

        private void MruSearchDialog_Deactivated(object sender, EventArgs e)
        {
            if (!_isClosing)
            {
                _isClosing = true;
                Close();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            PlaceholderText.Visibility = string.IsNullOrEmpty(SearchTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            FilterResults(SearchTextBox.Text);
        }

        private void FilterResults(string query)
        {
            IEnumerable<MruItem> filtered;

            if (string.IsNullOrEmpty(query))
            {
                filtered = _allItems;
            }
            else
            {
                var queryLower = query.ToLowerInvariant();
                filtered = _allItems.Where(item =>
                    item.DisplayNameLower.Contains(queryLower) ||
                    item.FullPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var results = filtered.ToList();
            ResultsListBox.ItemsSource = results;

            if (results.Count > 0)
            {
                ResultsListBox.SelectedIndex = 0;
                ResultsListBox.ScrollIntoView(ResultsListBox.Items[0]);
            }

            StatusText.Text = results.Count > 0
                ? $"{results.Count} recent item{(results.Count == 1 ? "" : "s")}"
                : string.IsNullOrEmpty(query)
                    ? "No recent solutions or folders"
                    : "No matching items";
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    SelectedItem = null;
                    _isClosing = true;
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
            {
                return;
            }

            var newIndex = ResultsListBox.SelectedIndex + delta;
            newIndex = Math.Max(0, Math.Min(newIndex, ResultsListBox.Items.Count - 1));
            ResultsListBox.SelectedIndex = newIndex;
            ResultsListBox.ScrollIntoView(ResultsListBox.Items[newIndex]);
        }

        private void SelectCurrentItem()
        {
            if (ResultsListBox.SelectedItem is MruItem item)
            {
                SelectedItem = item;
                _isClosing = true;
                DialogResult = true;
                Close();
            }
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectCurrentItem();
        }

        private void ResultsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SearchTextBox.Focus();
        }
    }
}
