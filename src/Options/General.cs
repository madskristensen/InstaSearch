using System.ComponentModel;

namespace InstaSearch.Options
{
    /// <summary>
    /// General options for InstaSearch. Also implements IRatingConfig for rating prompt support.
    /// </summary>
    internal class General : BaseOptionModel<General>, IRatingConfig
    {
        [Browsable(false)]
        public int RatingRequests { get; set; }
    }
}
