using System.Collections.Generic;
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

        public static readonly DependencyProperty HighlightBrushProperty =
            DependencyProperty.Register(
                nameof(HighlightBrush),
                typeof(Brush),
                typeof(HighlightTextBlock),
                new PropertyMetadata(new SolidColorBrush(Color.FromArgb(80, 255, 200, 0)), OnHighlightChanged));

        /// <summary>
        /// The text to highlight (search query).
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

            // Handle wildcard patterns
            if (highlight.Contains("*"))
            {
                HighlightWildcard(source, highlight);
            }
            else
            {
                HighlightSubstring(source, highlight);
            }
        }

        private void HighlightSubstring(string source, string highlight)
        {
            var sourceLower = source.ToLowerInvariant();
            var highlightLower = highlight.ToLowerInvariant();

            var lastIndex = 0;
            var index = sourceLower.IndexOf(highlightLower, StringComparison.Ordinal);

            while (index >= 0)
            {
                // Add text before match
                if (index > lastIndex)
                {
                    Inlines.Add(new Run(source.Substring(lastIndex, index - lastIndex)));
                }

                // Add highlighted match
                Inlines.Add(new Run(source.Substring(index, highlight.Length))
                {
                    Background = HighlightBrush,
                    FontWeight = FontWeights.SemiBold
                });

                lastIndex = index + highlight.Length;
                index = sourceLower.IndexOf(highlightLower, lastIndex, StringComparison.Ordinal);
            }

            // Add remaining text
            if (lastIndex < source.Length)
            {
                Inlines.Add(new Run(source.Substring(lastIndex)));
            }
        }

        private void HighlightWildcard(string source, string pattern)
        {
            var segments = pattern.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
            var sourceLower = source.ToLowerInvariant();

            if (segments.Length == 0)
            {
                Inlines.Add(new Run(source));
                return;
            }

            // Find all segment matches and their positions
            var matches = new List<(int Start, int Length)>();
            var searchStart = 0;

            foreach (var segment in segments)
            {
                var segmentLower = segment.ToLowerInvariant();
                var index = sourceLower.IndexOf(segmentLower, searchStart, StringComparison.Ordinal);

                if (index >= 0)
                {
                    matches.Add((index, segment.Length));
                    searchStart = index + segment.Length;
                }
            }

            // Build the highlighted text
            var lastIndex = 0;
            foreach (var (start, length) in matches)
            {
                // Add text before match
                if (start > lastIndex)
                {
                    Inlines.Add(new Run(source.Substring(lastIndex, start - lastIndex)));
                }

                // Add highlighted match
                Inlines.Add(new Run(source.Substring(start, length))
                {
                    Background = HighlightBrush,
                    FontWeight = FontWeights.SemiBold
                });

                lastIndex = start + length;
            }

            // Add remaining text
            if (lastIndex < source.Length)
            {
                Inlines.Add(new Run(source.Substring(lastIndex)));
            }
        }
    }
}
