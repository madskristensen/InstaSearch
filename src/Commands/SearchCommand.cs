using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using InstaSearch.Options;
using InstaSearch.Services;
using InstaSearch.UI;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace InstaSearch
{
    [Command(PackageIds.SearchCommand)]
    internal sealed class SearchCommand : BaseCommand<SearchCommand>
    {
        // Shared services for performance (reuse across invocations)
        // Note: The lambda defers reading options until indexing occurs, ensuring fresh values
        private static readonly FileIndexer _indexer = new(GetIgnoredFolders);
        private static readonly SearchHistoryService _history = new();
        private static readonly SearchService _searchService = new(_indexer, _history, GetIgnoredFilePatterns);
        private static readonly SearchRootResolver _rootResolver = new();
        private static readonly MruService _mruService = new();
        private static IVsImageService2 _imageService;
        private static RatingPrompt _ratingPrompt;
        private static int _warmupStarted;

        private static IgnoredFolderFilter GetIgnoredFolders() => General.Instance.GetIgnoredFolderFilter();
        private static IReadOnlyList<string> GetIgnoredFilePatterns() => General.Instance.GetIgnoredFilePatternsList();

        // Track the open dialog instance to prevent multiple windows
        private static SearchDialog _openDialog;

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // If dialog is already open, just activate it
            if (_openDialog != null)
            {
                _openDialog.Activate();
                return;
            }

            var rootPathsTask = _rootResolver.GetSearchRootsAsync();
            var mruItemsTask = _mruService.GetMruItemsAsync();
            var imageServiceTask = _imageService != null
                ? Task.FromResult(_imageService)
                : GetImageServiceAsync();

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    _imageService ??= await imageServiceTask;
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                }
            });

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    IReadOnlyList<string> rootPaths = await rootPathsTask;
                    var primaryRoot = rootPaths.Count > 0 ? rootPaths[0] : null;

                    if (!string.IsNullOrEmpty(primaryRoot))
                    {
                        _history.SetWorkspaceRoot(primaryRoot);
                        var mruPath = await _rootResolver.GetCurrentWorkspacePathForMruAsync();
                        await _mruService.RecordPathAsync(mruPath);
                    }
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                }
            });

            // Get the main VS window for positioning
            Window mainWindow = Application.Current.MainWindow;

            // Create and show the unified search dialog
            var dialog = new SearchDialog(_searchService, _mruService, rootPathsTask, mruItemsTask, imageServiceTask);

            if (mainWindow != null)
            {
                dialog.Owner = mainWindow;
            }

            dialog.Topmost = true;
            dialog.FilesSelected += OnFilesSelected;
            dialog.GoToLineRequested += OnGoToLineRequested;
            dialog.MruItemSelected += OnMruItemSelected;
            dialog.Closed += (s, args) => _openDialog = null;
            _openDialog = dialog;

            dialog.ShowDialog();
        }

        private static async System.Threading.Tasks.Task<IVsImageService2> GetImageServiceAsync()
        {
            IVsImageService2 imageService = await VS.GetServiceAsync<SVsImageService, IVsImageService2>();
            if (imageService == null)
            {
                throw new InvalidOperationException("Unable to retrieve Visual Studio image service.");
            }

            return imageService;
        }

        /// <summary>
        /// Opens the selected MRU item (solution or folder) in Visual Studio.
        /// </summary>
        private static async Task OpenMruItemAsync(MruItem item)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (item.Kind == MruItemKind.Folder)
                {
                    var solution = await VS.GetServiceAsync<SVsSolution, IVsSolution7>();
                    solution?.OpenFolder(item.FullPath);
                }
                else
                {
                    var dte = await VS.GetServiceAsync<EnvDTE.DTE, EnvDTE80.DTE2>();
                    dte?.Solution.Open(item.FullPath);
                }
            }
            catch (Exception ex)
            {
                await VS.StatusBar.ShowMessageAsync($"Error opening: {ex.Message}");
                await ex.LogAsync();
            }
        }

        private static void OnMruItemSelected(object sender, MruItemSelectedEventArgs e)
        {
            if (e?.SelectedItem == null)
            {
                return;
            }

            _ = ThreadHelper.JoinableTaskFactory.StartOnIdle(async () =>
            {
                try
                {
                    var shouldContinue = true;
                    var options = await General.GetLiveInstanceAsync();
                    if (options != null && options.ConfirmMruWorkspaceSwitch)
                    {
                        var currentWorkspacePath = await _rootResolver.GetCurrentWorkspacePathForMruAsync();
                        if (!string.IsNullOrWhiteSpace(currentWorkspacePath)
                            && !AreEquivalentPaths(currentWorkspacePath, e.SelectedItem.FullPath))
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                            var dialog = new MruWorkspaceSwitchDialog(e.SelectedItem.DisplayName, e.SelectedItem.FullPath)
                            {
                                Owner = Application.Current?.MainWindow
                            };

                            shouldContinue = dialog.ShowDialog() == true;

                            if (shouldContinue && dialog.DoNotShowAgain)
                            {
                                options.ConfirmMruWorkspaceSwitch = false;
                                options.Save();
                            }
                        }
                    }

                    if (!shouldContinue)
                    {
                        return;
                    }

                    await _mruService.RecordItemAsync(e.SelectedItem);
                    await OpenMruItemAsync(e.SelectedItem);
                }
                catch (Exception ex)
                {
                    await VS.StatusBar.ShowMessageAsync($"Error opening: {ex.Message}");
                    await ex.LogAsync();
                }
            });
        }

        private static bool AreEquivalentPaths(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            var normalizedLeft = NormalizePath(left);
            var normalizedRight = NormalizePath(right);

            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (ArgumentException)
            {
                return path;
            }
            catch (NotSupportedException)
            {
                return path;
            }
            catch (PathTooLongException)
            {
                return path;
            }
        }

        internal static void StartWarmupIfNeeded(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _warmupStarted, 1) != 0)
            {
                return;
            }

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    IReadOnlyList<string> roots = await _rootResolver.GetSearchRootsAsync();
                    await _searchService.WarmupIndexAsync(roots, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    await ex.LogAsync();
                }
            });
        }

        private static void OnFilesSelected(object sender, FilesSelectedEventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.StartOnIdle(async () =>
            {
                try
                {
                    IReadOnlyList<SearchResult> selectedFiles = e.SelectedFiles;
                    var lineNumber = e.LineNumber;
                    var columnNumber = e.ColumnNumber;

                    // Open all selected files
                    DocumentView lastDocumentView = null;
                    foreach (SearchResult file in selectedFiles)
                    {
                        // Record the selection for history
                        await _searchService.RecordSelectionAsync(file.FullPath);

                        // Open the file in VS
                        lastDocumentView = await VS.Documents.OpenAsync(file.FullPath);
                    }

                    // Navigate to specific line and column in the last opened file (typically the first selected)
                    // Line/column number only applies when a single file is selected
                    if (lineNumber.HasValue && selectedFiles.Count == 1 && lastDocumentView?.TextView != null)
                    {
                        await NavigateToLineAsync(lastDocumentView.TextView, lineNumber.Value, columnNumber);
                    }

                    // Register successful usage for rating prompt
                    _ratingPrompt ??= new RatingPrompt("MadsKristensen.InstaSearch", Vsix.Name, await General.GetLiveInstanceAsync());
                    _ratingPrompt.RegisterSuccessfulUsage();
                }
                catch (Exception ex)
                {
                    await VS.StatusBar.ShowMessageAsync($"Error opening file: {ex.Message}");
                    await ex.LogAsync();
                }
            });
        }

        private static void OnGoToLineRequested(object sender, GoToLineRequestedEventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.StartOnIdle(async () =>
            {
                try
                {
                    await NavigateToLineInCurrentDocumentAsync(e.LineNumber, e.ColumnNumber);

                    // Register successful usage for rating prompt
                    _ratingPrompt ??= new RatingPrompt("MadsKristensen.InstaSearch", Vsix.Name, await General.GetLiveInstanceAsync());
                    _ratingPrompt.RegisterSuccessfulUsage();
                }
                catch (Exception ex)
                {
                    await VS.StatusBar.ShowMessageAsync($"Error navigating to line: {ex.Message}");
                    await ex.LogAsync();
                }
            });
        }

        /// <summary>
        /// Navigates to a specific line and optional column number in the text view.
        /// </summary>
        /// <param name="textView">The text view to navigate in.</param>
        /// <param name="lineNumber">The 1-based line number to navigate to.</param>
        /// <param name="columnNumber">The 1-based column number to navigate to, or null for beginning of line.</param>
        private static async Task NavigateToLineAsync(IWpfTextView textView, int lineNumber, int? columnNumber = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                ITextSnapshot snapshot = textView.TextSnapshot;

                // Convert 1-based line number to 0-based index
                var lineIndex = lineNumber - 1;

                // Clamp to valid range
                if (lineIndex < 0)
                {
                    lineIndex = 0;
                }
                else if (lineIndex >= snapshot.LineCount)
                {
                    lineIndex = snapshot.LineCount - 1;
                }

                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineIndex);
                SnapshotPoint caretPosition = line.Start;

                // Calculate caret position: column is 1-based and means "after N characters"
                // e.g., column 3 = after 3 characters = position 3
                if (columnNumber.HasValue)
                {
                    caretPosition += Math.Max(0, Math.Min(columnNumber.Value, line.Length));
                }

                // Move caret to the calculated position
                textView.Caret.MoveTo(caretPosition);

                // Center the line in the view
                textView.ViewScroller.EnsureSpanVisible(
                    new SnapshotSpan(line.Start, line.End),
                    EnsureSpanVisibleOptions.AlwaysCenter);
            }
            catch (Exception)
            {
                // Silently fail if navigation fails - the file is still open
            }
        }

        /// <summary>
        /// Navigates to a specific line and optional column number in the current document.
        /// Shows a status bar message if no text document is open.
        /// </summary>
        /// <param name="lineNumber">The 1-based line number to navigate to.</param>
        /// <param name="columnNumber">The 1-based column number to navigate to, or null for beginning of line.</param>
        private static async Task NavigateToLineInCurrentDocumentAsync(int lineNumber, int? columnNumber = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            DocumentView documentView = await VS.Documents.GetActiveDocumentViewAsync();

            if (documentView?.TextView == null)
            {
                await VS.StatusBar.ShowMessageAsync("Go to line requires an open text document.");
                return;
            }

            await NavigateToLineAsync(documentView.TextView, lineNumber, columnNumber);

            var columnInfo = columnNumber.HasValue ? $":{columnNumber}" : "";
            await VS.StatusBar.ShowMessageAsync($"Line {lineNumber}{columnInfo}");
        }

        private sealed class MruWorkspaceSwitchDialog : Window
        {
            private readonly CheckBox _doNotShowAgainCheckBox;

            public bool DoNotShowAgain => _doNotShowAgainCheckBox.IsChecked == true;

            public MruWorkspaceSwitchDialog(string displayName, string fullPath)
            {
                if (displayName == null)
                {
                    throw new ArgumentNullException(nameof(displayName));
                }

                if (fullPath == null)
                {
                    throw new ArgumentNullException(nameof(fullPath));
                }

                Title = $"Confirm Workspace Switch - {displayName}";
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                SizeToContent = SizeToContent.WidthAndHeight;
                MinWidth = 520;
                MaxWidth = 760;
                ShowInTaskbar = false;
                ResizeMode = ResizeMode.NoResize;
                WindowStyle = WindowStyle.None;

                SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

                var outerBorder = new Border
                {
                    BorderThickness = new Thickness(1)
                };
                outerBorder.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);

                var chromeGrid = new Grid();
                chromeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                chromeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var titleBar = new Border
                {
                    Padding = new Thickness(12, 8, 4, 8)
                };
                titleBar.SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
                titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;

                var titleBarGrid = new Grid();
                titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titleBarText = new TextBlock
                {
                    Text = Title,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                titleBarText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetColumn(titleBarText, 0);
                titleBarGrid.Children.Add(titleBarText);

                Style createDialogButtonStyle(
                    object normalBackgroundKey,
                    object normalBorderKey,
                    object hoverBackgroundKey,
                    object hoverBorderKey,
                    object pressedBackgroundKey,
                    object pressedBorderKey)
                {
                    Setter createDynamicSetter(DependencyProperty property, object resourceKey)
                    {
                        var setter = new Setter(property, null);
                        setter.Value = new DynamicResourceExtension(resourceKey);
                        return setter;
                    }

                    ControlTemplate createDialogButtonTemplate()
                    {
                        var borderFactory = new FrameworkElementFactory(typeof(Border));
                        borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
                        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
                        borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
                        borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));

                        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                        contentPresenter.SetValue(MarginProperty, new TemplateBindingExtension(PaddingProperty));
                        contentPresenter.SetValue(ContentProperty, new TemplateBindingExtension(ContentProperty));
                        contentPresenter.SetValue(ContentTemplateProperty, new TemplateBindingExtension(ContentTemplateProperty));
                        contentPresenter.SetValue(HorizontalAlignmentProperty, new TemplateBindingExtension(HorizontalContentAlignmentProperty));
                        contentPresenter.SetValue(VerticalAlignmentProperty, new TemplateBindingExtension(VerticalContentAlignmentProperty));

                        borderFactory.AppendChild(contentPresenter);

                        return new ControlTemplate(typeof(Button))
                        {
                            VisualTree = borderFactory
                        };
                    }

                    var style = new Style(typeof(Button));
                    style.Setters.Add(createDynamicSetter(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey));
                    style.Setters.Add(createDynamicSetter(Control.BackgroundProperty, normalBackgroundKey));
                    style.Setters.Add(createDynamicSetter(Control.BorderBrushProperty, normalBorderKey));
                    style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                    style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
                    style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
                    style.Setters.Add(new Setter(Control.TemplateProperty, createDialogButtonTemplate()));

                    var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
                    hoverTrigger.Setters.Add(createDynamicSetter(Control.BackgroundProperty, hoverBackgroundKey));
                    hoverTrigger.Setters.Add(createDynamicSetter(Control.BorderBrushProperty, hoverBorderKey));
                    style.Triggers.Add(hoverTrigger);

                    var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
                    pressedTrigger.Setters.Add(createDynamicSetter(Control.BackgroundProperty, pressedBackgroundKey));
                    pressedTrigger.Setters.Add(createDynamicSetter(Control.BorderBrushProperty, pressedBorderKey));
                    style.Triggers.Add(pressedTrigger);

                    var disabledTrigger = new Trigger { Property = IsEnabledProperty, Value = false };
                    disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.56));
                    style.Triggers.Add(disabledTrigger);

                    return style;
                }

                Style createDialogCheckBoxStyle()
                {
                    Setter createDynamicSetter(DependencyProperty property, object resourceKey)
                    {
                        var setter = new Setter(property, null);
                        setter.Value = new DynamicResourceExtension(resourceKey);
                        return setter;
                    }

                    ControlTemplate createDialogCheckBoxTemplate()
                    {
                        var rootGrid = new FrameworkElementFactory(typeof(StackPanel));
                        rootGrid.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

                        var indicatorBorder = new FrameworkElementFactory(typeof(Border))
                        {
                            Name = "IndicatorBorder"
                        };
                        indicatorBorder.SetValue(FrameworkElement.WidthProperty, 12.0);
                        indicatorBorder.SetValue(FrameworkElement.HeightProperty, 12.0);
                        indicatorBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
                        indicatorBorder.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                        indicatorBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
                        indicatorBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
                        indicatorBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

                        var checkMark = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path))
                        {
                            Name = "CheckMark"
                        };
                        checkMark.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 1 6 L 4 9 L 10 2"));
                        checkMark.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.5);
                        checkMark.SetValue(System.Windows.Shapes.Path.SnapsToDevicePixelsProperty, true);
                        checkMark.SetValue(System.Windows.Shapes.Path.StretchProperty, Stretch.None);
                        checkMark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                        checkMark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                        checkMark.SetValue(System.Windows.Shapes.Path.StrokeProperty, new TemplateBindingExtension(Control.ForegroundProperty));
                        checkMark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
                        indicatorBorder.AppendChild(checkMark);

                        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                        contentPresenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
                        contentPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                        contentPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
                        contentPresenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentProperty));
                        contentPresenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentTemplateProperty));

                        rootGrid.AppendChild(indicatorBorder);
                        rootGrid.AppendChild(contentPresenter);

                        var template = new ControlTemplate(typeof(CheckBox))
                        {
                            VisualTree = rootGrid
                        };

                        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                        checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
                        template.Triggers.Add(checkedTrigger);

                        return template;
                    }

                    var style = new Style(typeof(CheckBox));
                    style.Setters.Add(createDynamicSetter(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey));
                    style.Setters.Add(createDynamicSetter(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey));
                    style.Setters.Add(createDynamicSetter(Control.BorderBrushProperty, EnvironmentColors.ToolWindowTextBrushKey));
                    style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                    style.Setters.Add(new Setter(Control.TemplateProperty, createDialogCheckBoxTemplate()));

                    var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
                    hoverTrigger.Setters.Add(createDynamicSetter(Control.BackgroundProperty, EnvironmentColors.CommandBarMouseOverBackgroundGradientBrushKey));
                    hoverTrigger.Setters.Add(createDynamicSetter(Control.BorderBrushProperty, EnvironmentColors.ToolWindowTextBrushKey));
                    style.Triggers.Add(hoverTrigger);

                    var disabledTrigger = new Trigger { Property = IsEnabledProperty, Value = false };
                    disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.56));
                    style.Triggers.Add(disabledTrigger);

                    return style;
                }

                var closeButton = new Button
                {
                    Content = "✕",
                    Width = 26,
                    Height = 24,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    ToolTip = "Close"
                };
                closeButton.Style = createDialogButtonStyle(
                    EnvironmentColors.ToolWindowBackgroundBrushKey,
                    EnvironmentColors.ToolWindowBorderBrushKey,
                    EnvironmentColors.CommandBarMouseOverBackgroundGradientBrushKey,
                    EnvironmentColors.ToolWindowBorderBrushKey,
                    EnvironmentColors.CommandBarSelectedBrushKey,
                    EnvironmentColors.FileTabSelectedBorderBrushKey);
                closeButton.Click += CancelButton_Click;
                Grid.SetColumn(closeButton, 1);
                titleBarGrid.Children.Add(closeButton);

                titleBar.Child = titleBarGrid;
                Grid.SetRow(titleBar, 0);
                chromeGrid.Children.Add(titleBar);

                var contentBorder = new Border
                {
                    Padding = new Thickness(16, 12, 16, 16)
                };

                var rootPanel = new StackPanel();

                var titleText = new TextBlock
                {
                    Text = "Open selected workspace item?",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold
                };
                titleText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                rootPanel.Children.Add(titleText);

                var description = new TextBlock
                {
                    Margin = new Thickness(0, 10, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.9,
                    Text = $"This will switch your current workspace context to: {displayName}"
                };
                description.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                rootPanel.Children.Add(description);

                var footerGrid = new Grid
                {
                    Margin = new Thickness(0, 22, 0, 0)
                };
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                _doNotShowAgainCheckBox = new CheckBox
                {
                    Content = "Don't show this again",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                _doNotShowAgainCheckBox.Style = createDialogCheckBoxStyle();
                _doNotShowAgainCheckBox.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                Grid.SetColumn(_doNotShowAgainCheckBox, 0);
                footerGrid.Children.Add(_doNotShowAgainCheckBox);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var continueButton = new Button
                {
                    Content = "Continue",
                    MinWidth = 90,
                    Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(10, 4, 10, 4),
                    IsDefault = true
                };
                continueButton.Style = createDialogButtonStyle(
                    EnvironmentColors.CommandBarSelectedBrushKey,
                    EnvironmentColors.FileTabSelectedBorderBrushKey,
                    EnvironmentColors.CommandBarHoverOverSelectedBrushKey,
                    EnvironmentColors.FileTabSelectedBorderBrushKey,
                    EnvironmentColors.CommandBarSelectedBrushKey,
                    EnvironmentColors.FileTabSelectedBorderBrushKey);
                continueButton.Click += ContinueButton_Click;

                var cancelButton = new Button
                {
                    Content = "Cancel",
                    MinWidth = 90,
                    Padding = new Thickness(10, 4, 10, 4),
                    IsCancel = true
                };
                cancelButton.Style = createDialogButtonStyle(
                    EnvironmentColors.ToolWindowBackgroundBrushKey,
                    EnvironmentColors.ToolWindowBorderBrushKey,
                    EnvironmentColors.CommandBarMouseOverBackgroundGradientBrushKey,
                    EnvironmentColors.ToolWindowBorderBrushKey,
                    EnvironmentColors.CommandBarSelectedBrushKey,
                    EnvironmentColors.FileTabSelectedBorderBrushKey);
                cancelButton.Click += CancelButton_Click;

                buttonPanel.Children.Add(continueButton);
                buttonPanel.Children.Add(cancelButton);
                Grid.SetColumn(buttonPanel, 1);
                footerGrid.Children.Add(buttonPanel);
                rootPanel.Children.Add(footerGrid);

                contentBorder.Child = rootPanel;
                Grid.SetRow(contentBorder, 1);
                chromeGrid.Children.Add(contentBorder);

                outerBorder.Child = chromeGrid;
                Content = outerBorder;
            }

            private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2)
                {
                    DialogResult = false;
                    Close();
                    return;
                }

                DragMove();
            }

            private void ContinueButton_Click(object sender, RoutedEventArgs e)
            {
                DialogResult = true;
                Close();
            }

            private void CancelButton_Click(object sender, RoutedEventArgs e)
            {
                DialogResult = false;
                Close();
            }
        }
    }
}
