using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell.Interop;
using static Microsoft.VisualStudio.VSConstants;

namespace InstaSearch.Services
{
    /// <summary>
    /// Reads the VS Most Recently Used (MRU) solutions and folders via IVsMRUItemsStore.
    /// Items are returned in MRU order (most recent first).
    /// </summary>
    public class MruService
    {
        private const uint _maxVsItems = 50;
        private const int _maxCustomItems = 200;
        private const string _customMruFolderName = "InstaSearch";
        private const string _customMruFileName = "InstaSearch.mru.txt";
        private const string _hiddenMruFileName = "InstaSearch.mru.hidden.txt";
        private static readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(12);
        private static readonly SemaphoreSlim _customMruLock = new(1, 1);
        private static DateTime _lastCleanupUtc = DateTime.MinValue;

        /// <summary>
        /// Reads MRU project/solution items from the VS MRU store.
        /// </summary>
        /// <returns>A list of parsed MRU entries in most-recently-used order.</returns>
        public async Task<IReadOnlyList<MruItem>> GetMruItemsAsync()
        {
            List<MruItem> customItems = await LoadCustomMruItemsAsync();
            customItems = await CleanupCustomMruIfNeededAsync(customItems);
            HashSet<string> hiddenPaths = await LoadHiddenMruPathsAsync();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            IVsMRUItemsStore store = await VS.GetServiceAsync<SVsMRUItemsStore, IVsMRUItemsStore>();
            List<MruItem> vsItems = [];

            if (store != null)
            {
                var buffer = new string[_maxVsItems];
                Guid projectsGuid = MruList.Projects;
                var count = store.GetMRUItems(ref projectsGuid, string.Empty, _maxVsItems, buffer);

                for (uint i = 0; i < count; i++)
                {
                    MruItem item = ParseMruEntry(buffer[i]);
                    if (item != null)
                    {
                        vsItems.Add(item);
                    }
                }
            }

            List<MruItem> merged = MergeAndDedupe(vsItems, customItems);
            if (hiddenPaths.Count == 0)
            {
                return merged;
            }

            return [.. merged.Where(item => !hiddenPaths.Contains(item.FullPath))];
        }

        /// <summary>
        /// Records a path in the custom MRU store.
        /// </summary>
        public async Task RecordPathAsync(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            var expandedPath = Environment.ExpandEnvironmentVariables(fullPath.Trim());
            MruItem item = CreateMruItemFromPath(expandedPath);
            if (item == null)
            {
                return;
            }

            await RecordItemAsync(item);
        }

        /// <summary>
        /// Records an MRU item in the custom MRU store.
        /// </summary>
        public async Task RecordItemAsync(MruItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.FullPath) || !ExistsOnDisk(item.FullPath))
            {
                return;
            }

            await _customMruLock.WaitAsync();
            try
            {
                List<MruItem> items = await ReadCustomMruItemsCoreAsync();
                items.RemoveAll(existing =>
                    string.Equals(existing.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase));

                if (item.Kind != MruItemKind.Folder)
                {
                    var parentPath = Path.GetDirectoryName(item.FullPath);
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        items.RemoveAll(existing =>
                            existing.Kind == MruItemKind.Folder &&
                            string.Equals(existing.FullPath, parentPath, StringComparison.OrdinalIgnoreCase));
                    }
                }

                items.Insert(0, item);

                if (items.Count > _maxCustomItems)
                {
                    items.RemoveRange(_maxCustomItems, items.Count - _maxCustomItems);
                }

                await WriteCustomMruItemsCoreAsync(items);
                await RemoveHiddenPathCoreAsync(item.FullPath);
            }
            finally
            {
                _customMruLock.Release();
            }
        }

        /// <summary>
        /// Removes a path from custom MRU and suppresses it from merged MRU results.
        /// </summary>
        public async Task RemovePathAsync(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            string normalized = NormalizePath(fullPath);

            await _customMruLock.WaitAsync();
            try
            {
                List<MruItem> items = await ReadCustomMruItemsCoreAsync();
                items.RemoveAll(existing =>
                    string.Equals(existing.FullPath, normalized, StringComparison.OrdinalIgnoreCase));

                await WriteCustomMruItemsCoreAsync(items);
                await AddHiddenPathCoreAsync(normalized);
            }
            finally
            {
                _customMruLock.Release();
            }

            await RemoveFromVisualStudioMruAsync(normalized);
        }

        /// <summary>
        /// Parses a single MRU registry value into an <see cref="MruItem"/>.
        /// </summary>
        /// <remarks>
        /// Format: path|{guid}|bool|displayName|...|{guid}
        /// The first segment is the path (may contain %UserProfile% etc.).
        /// The fourth segment is the display name.
        /// </remarks>
        private static MruItem ParseMruEntry(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var parts = raw.Split('|');
            if (parts.Length < 4)
            {
                return null;
            }

            var rawPath = Environment.ExpandEnvironmentVariables(parts[0]);
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return null;
            }

            var displayName = parts[3];
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = Path.GetFileNameWithoutExtension(rawPath);
            }

            var extension = Path.GetExtension(rawPath);
            MruItemKind kind;
            ImageMoniker moniker;

            if (Directory.Exists(rawPath) || string.IsNullOrEmpty(extension))
            {
                kind = MruItemKind.Folder;
                moniker = KnownMonikers.FolderOpened;
            }
            else if (IsSolutionFileExtension(extension))
            {
                kind = MruItemKind.Solution;
                moniker = KnownMonikers.Solution;
            }
            else
            {
                kind = MruItemKind.Project;
                moniker = KnownMonikers.Solution;
            }

            return new MruItem(rawPath, displayName, kind, moniker);
        }

        private static List<MruItem> MergeAndDedupe(IReadOnlyList<MruItem> primary, IReadOnlyList<MruItem> secondary)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<MruItem>(primary.Count + secondary.Count);

            AddUnique(primary);
            AddUnique(secondary);
            return merged;

            void AddUnique(IReadOnlyList<MruItem> items)
            {
                foreach (MruItem item in items)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.FullPath))
                    {
                        continue;
                    }

                    if (seen.Add(item.FullPath))
                    {
                        merged.Add(item);
                    }
                }
            }
        }

        private async Task<List<MruItem>> LoadCustomMruItemsAsync()
        {
            await _customMruLock.WaitAsync();
            try
            {
                return await ReadCustomMruItemsCoreAsync();
            }
            finally
            {
                _customMruLock.Release();
            }
        }

        private async Task<List<MruItem>> CleanupCustomMruIfNeededAsync(List<MruItem> customItems)
        {
            if (DateTime.UtcNow - _lastCleanupUtc < _cleanupInterval)
            {
                return customItems;
            }

            List<MruItem> cleaned = [.. customItems.Where(item => ExistsOnDisk(item.FullPath))];
            _lastCleanupUtc = DateTime.UtcNow;

            if (cleaned.Count == customItems.Count)
            {
                return cleaned;
            }

            await _customMruLock.WaitAsync();
            try
            {
                await WriteCustomMruItemsCoreAsync(cleaned);
            }
            finally
            {
                _customMruLock.Release();
            }

            return cleaned;
        }

        private static bool ExistsOnDisk(string fullPath)
        {
            return Directory.Exists(fullPath) || File.Exists(fullPath);
        }

        private static MruItem CreateMruItemFromPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !ExistsOnDisk(fullPath))
            {
                return null;
            }

            if (Directory.Exists(fullPath))
            {
                var folderName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    folderName = fullPath;
                }

                return new MruItem(fullPath, folderName, MruItemKind.Folder, KnownMonikers.FolderOpened);
            }

            var extension = Path.GetExtension(fullPath);
            var displayName = Path.GetFileNameWithoutExtension(fullPath);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = Path.GetFileName(fullPath);
            }

            var kind = IsSolutionFileExtension(extension)
                ? MruItemKind.Solution
                : MruItemKind.Project;

            return new MruItem(fullPath, displayName, kind, KnownMonikers.Solution);
        }

        private static string GetCustomMruFilePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(localAppData, _customMruFolderName);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, _customMruFileName);
        }

        private static string GetHiddenMruFilePath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(localAppData, _customMruFolderName);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, _hiddenMruFileName);
        }

        private static async Task<List<MruItem>> ReadCustomMruItemsCoreAsync()
        {
            return await Task.Run(() =>
            {
                var path = GetCustomMruFilePath();
                if (!File.Exists(path))
                {
                    return new List<MruItem>();
                }

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(path);
                }
                catch
                {
                    return new List<MruItem>();
                }

                var items = new List<MruItem>(lines.Length);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('|');
                    string pathValue;
                    if (separatorIndex > 0)
                    {
                        pathValue = line.Substring(separatorIndex + 1);
                    }
                    else
                    {
                        pathValue = line;
                    }

                    var item = CreateMruItemFromPath(pathValue);
                    if (item != null)
                    {
                        items.Add(item);
                    }
                }

                return items;
            });
        }

        private static async Task WriteCustomMruItemsCoreAsync(IReadOnlyList<MruItem> items)
        {
            await Task.Run(() =>
            {
                var path = GetCustomMruFilePath();
                IEnumerable<string> lines = items
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.FullPath))
                    .Select(item => $"{(int)item.Kind}|{item.FullPath}");

                File.WriteAllLines(path, lines);
            });
        }

        private static async Task<HashSet<string>> LoadHiddenMruPathsAsync()
        {
            return await Task.Run(() =>
            {
                var path = GetHiddenMruFilePath();
                var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!File.Exists(path))
                {
                    return hidden;
                }

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(path);
                }
                catch
                {
                    return hidden;
                }

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    hidden.Add(NormalizePath(line));
                }

                return hidden;
            });
        }

        private static async Task AddHiddenPathCoreAsync(string fullPath)
        {
            HashSet<string> hidden = await LoadHiddenMruPathsAsync();
            hidden.Add(NormalizePath(fullPath));
            await WriteHiddenMruPathsAsync(hidden);
        }

        private static async Task RemoveHiddenPathCoreAsync(string fullPath)
        {
            HashSet<string> hidden = await LoadHiddenMruPathsAsync();
            hidden.Remove(NormalizePath(fullPath));
            await WriteHiddenMruPathsAsync(hidden);
        }

        private static async Task WriteHiddenMruPathsAsync(HashSet<string> hidden)
        {
            await Task.Run(() =>
            {
                var path = GetHiddenMruFilePath();
                File.WriteAllLines(path, hidden.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            });
        }

        private static string NormalizePath(string fullPath)
        {
            return Environment.ExpandEnvironmentVariables(fullPath.Trim());
        }

        private static bool IsSolutionFileExtension(string extension)
        {
            return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task RemoveFromVisualStudioMruAsync(string fullPath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                IVsMRUItemsStore store = await VS.GetServiceAsync<SVsMRUItemsStore, IVsMRUItemsStore>();
                if (store == null)
                {
                    return;
                }

                Guid projectsGuid = MruList.Projects;
                store.DeleteMRUItem(ref projectsGuid, fullPath);
            }
            catch
            {
                // Ignore VS MRU remove failures - custom suppression still applies
            }
        }
    }
}
