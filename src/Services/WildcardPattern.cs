namespace InstaSearch.Services
{
    /// <summary>
    /// Pre-parsed wildcard pattern that avoids allocations during matching.
    /// Parse once, match many times.
    /// </summary>
    internal readonly struct WildcardPattern
    {
        private static readonly char[] _wildcardSeparator = ['*'];

        public readonly string[] Segments;
        public readonly bool StartsWithWildcard;
        public readonly bool EndsWithWildcard;
        public readonly string FirstSegment;

        public WildcardPattern(string pattern)
        {
            Segments = pattern.Split(_wildcardSeparator, StringSplitOptions.None);
            StartsWithWildcard = pattern.Length > 0 && pattern[0] == '*';
            EndsWithWildcard = pattern.Length > 0 && pattern[pattern.Length - 1] == '*';

            // Cache first non-empty segment for StartsWith ranking
            FirstSegment = null;
            foreach (var seg in Segments)
            {
                if (seg.Length > 0)
                {
                    FirstSegment = seg;
                    break;
                }
            }
        }

        /// <summary>
        /// Matches the pre-parsed pattern against a filename. Zero allocations.
        /// </summary>
        public bool Matches(string fileName)
        {
            var pos = 0;

            for (var i = 0; i < Segments.Length; i++)
            {
                var segment = Segments[i];
                if (segment.Length == 0)
                    continue;

                var index = fileName.IndexOf(segment, pos, StringComparison.Ordinal);
                if (index < 0)
                    return false;

                // First segment must match at start if pattern doesn't start with *
                if (i == 0 && !StartsWithWildcard && index != 0)
                    return false;

                pos = index + segment.Length;
            }

            // Last segment must match at end if pattern doesn't end with *
            if (Segments.Length > 0 && !EndsWithWildcard)
            {
                var lastSegment = Segments[Segments.Length - 1];
                if (lastSegment.Length > 0 && !fileName.EndsWith(lastSegment, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if filename starts with the first segment (for ranking).
        /// </summary>
        public bool StartsWithFirstSegment(string fileName)
        {
            if (StartsWithWildcard || FirstSegment == null)
                return false;
            return fileName.StartsWith(FirstSegment, StringComparison.Ordinal);
        }
    }
}
