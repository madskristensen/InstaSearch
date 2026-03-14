using InstaSearch.Services;

namespace InstaSearch.UI
{
    /// <summary>
    /// Event args for when an MRU item is selected in the search dialog.
    /// </summary>
    public class MruItemSelectedEventArgs(MruItem selectedItem) : EventArgs
    {
        public MruItem SelectedItem { get; } = selectedItem;
    }
}
