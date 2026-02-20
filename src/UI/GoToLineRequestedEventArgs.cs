namespace InstaSearch.UI;

/// <summary>
/// Event args for when a go-to-line request is made in the search dialog.
/// This is triggered when the user types ":lineNumber" or ":lineNumber:columnNumber" to navigate
/// within the current document without specifying a file.
/// </summary>
public class GoToLineRequestedEventArgs(int lineNumber, int? columnNumber) : EventArgs
{
    /// <summary>
    /// Gets the 1-based line number to navigate to.
    /// </summary>
    public int LineNumber { get; } = lineNumber;

    /// <summary>
    /// Gets the 1-based column number to navigate to, or null if not specified.
    /// </summary>
    public int? ColumnNumber { get; } = columnNumber;
}
