using CommunityToolkit.Mvvm.ComponentModel;
using Heroesprofile.Uploader.Common;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Heroesprofile.Uploader.Desktop.Gui.ViewModels
{
    /// <summary>
    /// One row of the replay list. Theme 1 shows it the WPF way: file name on the left, a ✘ if the
    /// uploader deleted the file, and the status on the right in its status colour. Theme 2 (PR #53's
    /// design) splits the name into time and map and shows the status as a coloured badge.
    /// </summary>
    public partial class ReplayRowViewModel : ObservableObject
    {
        private const string TimeFormat = "yyyy-MM-dd HH.mm.ss";

        public ReplayFile File { get; }

        /// <summary>WPF's FilenameConverter: just the file name, no folder.</summary>
        public string FileName { get; }

        /// <summary>
        /// Theme 2: the game's time and map, from the file name the game gives replays
        /// ("yyyy-MM-dd HH.mm.ss &lt;Map&gt;.StormReplay"). A name that doesn't follow it (renamed file)
        /// shows the file's date and its whole name instead.
        /// </summary>
        public string TimeText { get; }
        public string MapText { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusLabel), nameof(IsSuccess), nameof(IsInProgress), nameof(IsNeutral), nameof(IsFailed))]
        private UploadStatus uploadStatus;

        [ObservableProperty]
        private bool deleted;

        public ReplayRowViewModel(ReplayFile file)
        {
            File = file;
            FileName = Path.GetFileName(file.Filename ?? "");
            uploadStatus = file.UploadStatus;
            deleted = file.Deleted;
            (TimeText, MapText) = SplitName(file);
        }

        internal static (string Time, string Map) SplitName(ReplayFile file)
        {
            var name = Path.GetFileNameWithoutExtension(file.Filename ?? "");
            if (name.Length > TimeFormat.Length &&
                DateTime.TryParseExact(name.Substring(0, TimeFormat.Length), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
                return (name.Substring(0, TimeFormat.Length), name.Substring(TimeFormat.Length).Trim());
            }
            return (file.Created.ToString(TimeFormat, CultureInfo.InvariantCulture), name);
        }

        /// <summary>Theme 2's badge text - PR #53's short labels ("Queued" for a replay not looked at yet).</summary>
        public string StatusLabel => ShortLabel(UploadStatus);

        public static string ShortLabel(UploadStatus status) => status switch {
            UploadStatus.Success => "Success",
            UploadStatus.Duplicate => "Duplicate",
            UploadStatus.InProgress => "In progress",
            UploadStatus.UploadError => "Upload error",
            UploadStatus.AiDetected => "AI detected",
            UploadStatus.Incomplete => "Incomplete",
            UploadStatus.PtrRegion => "PTR",
            UploadStatus.TooOld => "Too old",
            UploadStatus.CustomGame => "Custom game",
            UploadStatus.Brawl => "Brawl",
            _ => "Queued",
        };

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
