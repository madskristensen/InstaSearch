using System.Collections.Generic;
using System.IO;
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
        private const uint _maxItems = 50;

        /// <summary>
        /// Reads MRU project/solution items from the VS MRU store.
        /// </summary>
        /// <returns>A list of parsed MRU entries in most-recently-used order.</returns>
        public async Task<IReadOnlyList<MruItem>> GetMruItemsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            IVsMRUItemsStore store = await VS.GetServiceAsync<SVsMRUItemsStore, IVsMRUItemsStore>();
            if (store == null)
            {
                return [];
            }

            var items = new List<MruItem>();
            var buffer = new string[_maxItems];

            Guid projectsGuid = MruList.Projects;
            var count = store.GetMRUItems(ref projectsGuid, string.Empty, _maxItems, buffer);

            for (uint i = 0; i < count; i++)
            {
                MruItem item = ParseMruEntry(buffer[i]);
                if (item != null)
                {
                    items.Add(item);
                }
            }

            return items;
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

            if (string.IsNullOrEmpty(extension))
            {
                kind = MruItemKind.Folder;
                moniker = KnownMonikers.FolderOpened;
            }
            else
            {
                kind = MruItemKind.Solution;
                moniker = KnownMonikers.Solution;
            }

            return new MruItem(rawPath, displayName, kind, moniker);
        }
    }
}
