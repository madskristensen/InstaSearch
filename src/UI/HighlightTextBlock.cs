using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace InstaSearch.UI
{
    /// <summary>
    /// A TextBlock that highlights portions of text matching a search query.
    /// </summary>
    public class HighlightTextBlock : TextBlock
    {
        public static readonly DependencyProperty HighlightTextProperty =
            DependencyProperty.Register(
                nameof(HighlightText),
                typeof(string),
                typeof(HighlightTextBlock),
                new PropertyMetadata(string.Empty, OnHighlightChanged));

        public static readonly DependencyProperty SourceTextProperty =
            DependencyProperty.Register(
                nameof(SourceText),
                typeof(string),
                typeof(HighlightTextBlock),
                new PropertyMetadata(string.Empty, OnHighlightChanged));

        public static readonly DependencyProperty SourceTextLowerProperty =
            DependencyProperty.Register(
                nameof(SourceTextLower),
                typeof(string),
                typeof(HighlightTextBlock),
                new PropertyMetadata(string.Empty, OnHighlightChanged));

        public static readonly DependencyProperty HighlightBrushProperty =
            DependencyProperty.Register(
                nameof(HighlightBrush),
                typeof(Brush),
                typeof(HighlightTextBlock),
                new PropertyMetadata(new SolidColorBrush(Color.FromArgb(80, 255, 200, 0)), OnHighlightChanged));

        /// <summary>
        /// The text to highlight (search query, should be pre-lowercased).
        /// </summary>
        public string HighlightText
        {
            get => (string)GetValue(HighlightTextProperty);
            set => SetValue(HighlightTextProperty, value);
        }

        /// <summary>
        /// The source text to display.
        /// </summary>
        public string SourceText
        {
            get => (string)GetValue(SourceTextProperty);
            set => SetValue(SourceTextProperty, value);
        }

        /// <summary>
        /// The pre-lowercased source text for matching (avoids allocation).
        /// </summary>
        public string SourceTextLower
        {
            get => (string)GetValue(SourceTextLowerProperty);
            set => SetValue(SourceTextLowerProperty, value);
        }

        /// <summary>
        /// The brush used to highlight matching text.
        /// </summary>
        public Brush HighlightBrush
        {
            get => (Brush)GetValue(HighlightBrushProperty);
            set => SetValue(HighlightBrushProperty, value);
        }

        private static void OnHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HighlightTextBlock control)
            {
                control.UpdateHighlighting();
            }
        }

        private void UpdateHighlighting()
        {
            Inlines.Clear();

            var source = SourceText ?? string.Empty;
            var highlight = HighlightText ?? string.Empty;

            if (string.IsNullOrEmpty(source))
            {
                return;
            }

            if (string.IsNullOrEmpty(highlight))
            {
                Inlines.Add(new Run(source));
                return;
            }

            // Use pre-lowercased source if available and valid, otherwise lowercase now
            var sourceLower = SourceTextLower;
            if (string.IsNullOrEmpty(sourceLower) || sourceLower.Length != source.Length)
            {
                sourceLower = source.ToLowerInvariant();
            }

            // Handle wildcard patterns
            if (highlight.Contains("*"))
            {
                HighlightWildcard(source, sourceLower, highlight);
            }
            else
            {
                HighlightSubstring(source, sourceLower, highlight);
            }
        }

        private void HighlightSubstring(string source, string sourceLower, string highlightLower)
        {
            var lastIndex = 0;
            var index = sourceLower.IndexOf(highlightLower, StringComparison.Ordinal);

            while (index >= 0)
            {
                // Add text before match
                if (index > lastIndex && index <= source.Length)
                {
                    var length = Math.Min(index - lastIndex, source.Length - lastIndex);
                    if (length > 0)
                    {
                        Inlines.Add(new Run(source.Substring(lastIndex, length)));
                    }
                }

                // Add highlighted match (with bounds check)
                var matchLength = Math.Min(highlightLower.Length, source.Length - index);
                if (index < source.Length && matchLength > 0)
                {
                    Inlines.Add(new Run(source.Substring(index, matchLength))
                    {
                        Background = HighlightBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                }

                lastIndex = index + highlightLower.Length;
                if (lastIndex >= sourceLower.Length)
                    break;
                index = sourceLower.IndexOf(highlightLower, lastIndex, StringComparison.Ordinal);
            }

            // Add remaining text
            if (lastIndex < source.Length)
            {
                Inlines.Add(new Run(source.Substring(lastIndex)));
            }
        }

        private static readonly char[] _wildcardSeparator = ['*'];

        private void HighlightWildcard(string source, string sourceLower, string pattern)
        {
            var segments = pattern.Split(_wildcardSeparator, StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
            {
                Inlines.Add(new Run(source));
                return;
            }

            // Find matches and build output in single pass (avoid List allocation for small segment counts)
            var lastIndex = 0;
            var searchStart = 0;

            foreach (var segment in segments)
            {
                if (searchStart >= sourceLower.Length)
                    break;

                var index = sourceLower.IndexOf(segment, searchStart, StringComparison.Ordinal);

                if (index >= 0 && index < source.Length)
                {
                    // Add text before match (with bounds check)
                    if (index > lastIndex)
                    {
                        var length = Math.Min(index - lastIndex, source.Length - lastIndex);
                        if (length > 0)
                        {
                            Inlines.Add(new Run(source.Substring(lastIndex, length)));
                        }
                    }

                    // Add highlighted match (with bounds check)
                    var matchLength = Math.Min(segment.Length, source.Length - index);
                    if (matchLength > 0)
                    {
                        Inlines.Add(new Run(source.Substring(index, matchLength))
                        {
                            Background = HighlightBrush,
                            FontWeight = FontWeights.SemiBold
                        });
                    }

                    lastIndex = index + segment.Length;
                    searchStart = lastIndex;
                }
            }

            // Add remaining text
            if (lastIndex < source.Length)
            {
                Inlines.Add(new Run(source.Substring(lastIndex)));
            }
        }
    }
}
