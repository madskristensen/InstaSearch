using InstaSearch.Services;
using Microsoft.VisualStudio.Imaging.Interop;

namespace InstaSearch.UI
{
    internal enum SearchDialogItemKind
    {
        File,
        Mru
    }

    internal sealed class SearchDialogItem
    {
        private SearchDialogItem(SearchDialogItemKind kind)
        {
            Kind = kind;
        }

        public SearchDialogItemKind Kind { get; }
        public SearchResult FileResult { get; private set; }
        public MruItem MruItem { get; private set; }

        public string FileName => FileResult?.FileName;
        public string FileNameLower => FileResult?.FileNameLower;
        public string RelativePath => FileResult?.RelativePath;
        public string QueryLower => FileResult?.QueryLower ?? string.Empty;

        public string DisplayName => MruItem?.DisplayName;
        public string FullPath => Kind == SearchDialogItemKind.File ? FileResult?.FullPath : MruItem?.FullPath;

        public string SecondaryText => Kind == SearchDialogItemKind.File ? RelativePath : MruItem?.FullPath;

        public ImageMoniker Moniker => Kind == SearchDialogItemKind.File ? FileResult.Moniker : MruItem.Moniker;

        public bool IsFile => Kind == SearchDialogItemKind.File;
        public bool IsMru => Kind == SearchDialogItemKind.Mru;

        public static SearchDialogItem FromFile(SearchResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            return new SearchDialogItem(SearchDialogItemKind.File)
            {
                FileResult = result
            };
        }

        public static SearchDialogItem FromMru(MruItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return new SearchDialogItem(SearchDialogItemKind.Mru)
            {
                MruItem = item
            };
        }
    }
}
