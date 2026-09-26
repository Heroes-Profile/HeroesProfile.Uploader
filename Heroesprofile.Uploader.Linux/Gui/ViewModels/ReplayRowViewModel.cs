using CommunityToolkit.Mvvm.ComponentModel;
using Heroesprofile.Uploader.Common;
using System;
using System.Globalization;
using System.IO;

namespace Heroesprofile.Uploader.Linux.Gui.ViewModels
{
    /// <summary>
    /// Display wrapper around a single <see cref="ReplayFile"/> for the (virtualized) replay list.
    /// Filenames are "yyyy-MM-dd HH.mm.ss &lt;Map&gt;.StormReplay" - see Manager/LiveMonitor, which
    /// write replays out under exactly that name.
    /// </summary>
    public partial class ReplayRowViewModel : ObservableObject
    {
        private const string TimeFormat = "yyyy-MM-dd HH.mm.ss";

        public ReplayFile File { get; }
        public string TimeText { get; }
        public string MapText { get; }

        [ObservableProperty]
        private UploadStatus uploadStatus;

        public string StatusLabel => StatusPresentation.Label(UploadStatus);

        public ReplayRowViewModel(ReplayFile file)
        {
            File = file;
            uploadStatus = file.UploadStatus;

            var name = Path.GetFileNameWithoutExtension(file.Filename ?? "");
            if (name.Length > TimeFormat.Length &&
                DateTime.TryParseExact(name.Substring(0, TimeFormat.Length), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
                TimeText = parsed.ToString(TimeFormat);
                MapText = name.Substring(TimeFormat.Length).Trim();
            } else {
                // Doesn't match the expected pattern (renamed file, odd import) - fall back to
                // whatever we do know rather than showing a blank row.
                TimeText = file.Created.ToString(TimeFormat);
                MapText = name;
            }
        }

        /// <summary>Called by the Manager.Files adapter when the wrapped file's own status changes.</summary>
        public void RefreshFromFile()
        {
            UploadStatus = File.UploadStatus;
        }

        partial void OnUploadStatusChanged(UploadStatus value)
        {
            OnPropertyChanged(nameof(StatusLabel));
        }

        /// <summary>Forces the UploadStatus-bound badge colour bindings to re-run after a theme switch.</summary>
        public void TouchForThemeRefresh()
        {
            OnPropertyChanged(nameof(UploadStatus));
        }
    }
}
