using CommunityToolkit.Mvvm.ComponentModel;
using Heroesprofile.Uploader.Common;

namespace Heroesprofile.Uploader.Linux.Gui.ViewModels
{
    /// <summary>One tile in the header's 4-column stats grid - a status and its running total.</summary>
    public partial class StatChipViewModel : ObservableObject
    {
        public UploadStatus Status { get; }
        public string Label { get; }

        [ObservableProperty]
        private int total;

        public StatChipViewModel(UploadStatus status)
        {
            Status = status;
            Label = StatusPresentation.Label(status);
        }

        /// <summary>Forces the Status-bound dot-colour binding to re-run after a theme switch.</summary>
        public void TouchForThemeRefresh()
        {
            OnPropertyChanged(nameof(Status));
        }
    }
}
