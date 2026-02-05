using System.Runtime.InteropServices;

namespace InstaSearch.Options
{
    /// <summary>
    /// Provides options pages for the InstaSearch extension.
    /// </summary>
    internal class OptionsProvider
    {
        /// <summary>
        /// Options page for General settings.
        /// </summary>
        [ComVisible(true)]
        public class GeneralOptions : BaseOptionPage<General>
        {
        }
    }
}
