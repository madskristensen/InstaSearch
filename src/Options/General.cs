using System.ComponentModel;

namespace InstaSearch.Options
{
    /// <summary>
    /// General options for InstaSearch. Also implements IRatingConfig for rating prompt support.
    /// </summary>
    internal class General : BaseOptionModel<General>, IRatingConfig
    {
        private const double _defaultWidth = 600;
        private const double _defaultHeight = 400;
        private const double _minWidth = 400;
        private const double _minHeight = 250;
        private const double _maxWidth = 1200;
        private const double _maxHeight = 800;

        [Browsable(false)]
        public int RatingRequests { get; set; }

        [Browsable(false)]
        public double DialogWidth { get; set; } = _defaultWidth;

        [Browsable(false)]
        public double DialogHeight { get; set; } = _defaultHeight;

        /// <summary>
        /// Gets the dialog width, clamped to valid range.
        /// </summary>
        public double GetDialogWidth() => Math.Max(_minWidth, Math.Min(_maxWidth, DialogWidth));

        /// <summary>
        /// Gets the dialog height, clamped to valid range.
        /// </summary>
        public double GetDialogHeight() => Math.Max(_minHeight, Math.Min(_maxHeight, DialogHeight));
    }
}
