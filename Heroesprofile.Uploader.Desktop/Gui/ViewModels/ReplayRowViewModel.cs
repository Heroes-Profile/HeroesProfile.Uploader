using CommunityToolkit.Mvvm.ComponentModel;
using Heroesprofile.Uploader.Common;
using System.IO;
using System.Text.RegularExpressions;

namespace Heroesprofile.Uploader.Desktop.Gui.ViewModels
{
    /// <summary>
    /// One row of the replay list, shown the WPF way: file name on the left, a ✘ if the uploader
    /// deleted the file, and the status on the right in its status colour.
    /// </summary>
    public partial class ReplayRowViewModel : ObservableObject
    {
        public ReplayFile File { get; }

        /// <summary>WPF's FilenameConverter: just the file name, no folder.</summary>
        public string FileName { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsSuccess), nameof(IsInProgress), nameof(IsNeutral), nameof(IsFailed))]
        private UploadStatus uploadStatus;

        [ObservableProperty]
        private bool deleted;

        public ReplayRowViewModel(ReplayFile file)
        {
            File = file;
            FileName = Path.GetFileName(file.Filename ?? "");
            uploadStatus = file.UploadStatus;
            deleted = file.Deleted;
        }

        /// <summary>
        /// WPF's UploadStatusConverter: the enum name split into words ("UploadError" -> "Upload error"),
        /// and nothing at all for a replay that hasn't been looked at yet.
        /// </summary>
        public string StatusText => FormatStatus(UploadStatus);

        public static string FormatStatus(UploadStatus status) => status == UploadStatus.None
            ? ""
            : Regex.Replace(status.ToString(), "([a-z])([A-Z])", m => $"{m.Groups[1].Value} {m.Groups[2].Value.ToLowerInvariant()}");

        // WPF's UploadColorConverter buckets - each picks a StatusUpload*Brush via a style class, so
        // the colour follows theme switches without any manual refresh.
        public bool IsSuccess => UploadStatus == UploadStatus.Success;
        public bool IsInProgress => UploadStatus == UploadStatus.InProgress;
        public bool IsNeutral => UploadStatus is UploadStatus.Duplicate or UploadStatus.AiDetected or UploadStatus.CustomGame
            or UploadStatus.PtrRegion or UploadStatus.TooOld or UploadStatus.Brawl;
        public bool IsFailed => !IsSuccess && !IsInProgress && !IsNeutral;

        /// <summary>Called by <see cref="ReplayListBridge"/> when the wrapped file's status or Deleted flag changes.</summary>
        public void RefreshFromFile()
        {
            UploadStatus = File.UploadStatus;
            Deleted = File.Deleted;
        }
    }
}
