using CommunityToolkit.Mvvm.ComponentModel;
using Heroesprofile.Uploader.Common;

namespace Heroesprofile.Uploader.Desktop.Gui.ViewModels
{
    /// <summary>One "Success: 12" line under the status in the side panel - hidden while the count is zero, as in WPF.</summary>
    public partial class StatusCountViewModel : ObservableObject
    {
        /// <summary>The side panel's lines, in the WPF app's order and wording.</summary>
        public static readonly (UploadStatus Status, string Label)[] Lines = {
            (UploadStatus.None, "Not processed"),
            (UploadStatus.Success, "Success"),
            (UploadStatus.UploadError, "Upload error"),
            (UploadStatus.Duplicate, "Duplicate"),
            (UploadStatus.AiDetected, "Ai detected"),
            (UploadStatus.CustomGame, "Custom game"),
            (UploadStatus.PtrRegion, "Ptr region"),
            (UploadStatus.TooOld, "Too old"),
            (UploadStatus.Incomplete, "Incomplete"),
            (UploadStatus.Brawl, "Brawl"),
        };

        /// <summary>
        /// Theme 2's 4-column stats grid (PR #53), left to right, top to bottom - with Brawl in place of
        /// PR #53's "Too old", which only pre-alpha and ancient-build replays get (still counted in
        /// Theme 1's side panel if one turns up).
        /// </summary>
        public static readonly UploadStatus[] ChipOrder = {
            UploadStatus.Success, UploadStatus.Duplicate, UploadStatus.InProgress, UploadStatus.UploadError,
            UploadStatus.AiDetected, UploadStatus.Incomplete, UploadStatus.PtrRegion, UploadStatus.Brawl,
        };

        public StatusCountViewModel(UploadStatus status, string label)
        {
            Status = status;
            Label = label;
        }

        public UploadStatus Status { get; }
        public string Label { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Text), nameof(IsVisible))]
        private int count;

        public string Text => $"{Label}: {Count}";
        public bool IsVisible => Count > 0;
    }
}
