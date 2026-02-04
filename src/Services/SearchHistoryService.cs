using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace InstaSearch.Services
{
    /// <summary>
    /// Manages search history to bubble up frequently selected files.
    /// History is persisted per-workspace in the .vs folder.
    /// </summary>
    public class SearchHistoryService
    {
        private const int MaxHistoryEntries = 500;
        private const string HistoryFileName = "InstaSearch.history.txt";
        private const string VsFolderName = ".vs";
        private const string InstaSearchFolderName = "InstaSearch";

        private string _historyFilePath;
        private string _currentRootPath;
        private readonly Dictionary<string, int> _selectionCounts;
        private readonly object _lock = new();
        private bool _isDirty;

        public SearchHistoryService()
        {
            _selectionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the workspace root and loads history for that workspace.
        /// </summary>
        public void SetWorkspaceRoot(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || rootPath == _currentRootPath)
                return;

            lock (_lock)
            {
                _currentRootPath = rootPath;

                // Create .vs/InstaSearch folder
                var vsFolder = Path.Combine(rootPath, VsFolderName, InstaSearchFolderName);
                Directory.CreateDirectory(vsFolder);
                _historyFilePath = Path.Combine(vsFolder, HistoryFileName);

                // Clear and reload history for this workspace
                _selectionCounts.Clear();
                _isDirty = false;
                LoadHistory();
            }
        }

        /// <summary>
        /// Records that a file was selected, incrementing its priority score.
        /// </summary>
        public void RecordSelection(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(_historyFilePath))
                return;

            lock (_lock)
            {
                if (_selectionCounts.TryGetValue(fullPath, out var count))
                {
                    _selectionCounts[fullPath] = count + 1;
                }
                else
                {
                    _selectionCounts[fullPath] = 1;
                }
                _isDirty = true;

                // Trim if too large
                if (_selectionCounts.Count > MaxHistoryEntries)
                {
                    TrimHistory();
                }
            }
        }

        /// <summary>
        /// Gets the selection count (priority score) for a file.
        /// </summary>
        public int GetSelectionCount(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return 0;

            lock (_lock)
            {
                return _selectionCounts.TryGetValue(fullPath, out var count) ? count : 0;
            }
        }

        /// <summary>
        /// Saves history to disk if there are pending changes.
        /// </summary>
        public async Task SaveAsync()
        {
            List<KeyValuePair<string, int>> toSave;
            string filePath;

            lock (_lock)
            {
                if (!_isDirty || string.IsNullOrEmpty(_historyFilePath))
                    return;

                toSave = _selectionCounts.ToList();
                filePath = _historyFilePath;
                _isDirty = false;
            }

            await Task.Run(() =>
            {
                try
                {
                    IEnumerable<string> lines = toSave.Select(kvp => $"{kvp.Value}|{kvp.Key}");
                    File.WriteAllLines(filePath, lines);
                }
                catch
                {
                    // Ignore save errors - history is not critical
                }
            });
        }

        private void LoadHistory()
        {
            try
            {
                if (string.IsNullOrEmpty(_historyFilePath) || !File.Exists(_historyFilePath))
                    return;

                var lines = File.ReadAllLines(_historyFilePath);
                foreach (var line in lines)
                {
                    var separatorIndex = line.IndexOf('|');
                    if (separatorIndex > 0)
                    {
                        var countStr = line.Substring(0, separatorIndex);
                        var path = line.Substring(separatorIndex + 1);
                        if (int.TryParse(countStr, out var count) && !string.IsNullOrEmpty(path))
                        {
                            _selectionCounts[path] = count;
                        }
                    }
                }
            }
            catch
            {
                // Ignore load errors - start with empty history
            }
        }

        private void TrimHistory()
        {
            // Keep only the most frequently selected entries
            var toKeep = _selectionCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(MaxHistoryEntries / 2)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            _selectionCounts.Clear();
            foreach (KeyValuePair<string, int> kvp in toKeep)
            {
                _selectionCounts[kvp.Key] = kvp.Value;
            }
        }
    }
}
