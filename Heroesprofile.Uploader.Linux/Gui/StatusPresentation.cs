using Heroesprofile.Uploader.Common;

namespace Heroesprofile.Uploader.Linux.Gui
{
    /// <summary>
    /// Maps <see cref="UploadStatus"/> to the label/colour pairs shown in the stats grid and badges.
    /// Manager.Aggregates in practice (see Analyzer.GetPreStatus) only ever holds 8
    /// of the enum's 9 non-None values; CustomGame is never actually assigned by Analyzer today, so
    /// it and None (not-yet-processed, briefly true for queued replays) fall back to a neutral look.
    /// </summary>
    internal static class StatusPresentation
    {
        public static string Label(UploadStatus status)
        {
            switch (status) {
                case UploadStatus.Success: return "Success";
                case UploadStatus.Duplicate: return "Duplicate";
                case UploadStatus.InProgress: return "In progress";
                case UploadStatus.UploadError: return "Upload error";
                case UploadStatus.AiDetected: return "AI detected";
                case UploadStatus.Incomplete: return "Incomplete";
                case UploadStatus.PtrRegion: return "PTR";
                case UploadStatus.TooOld: return "Too old";
                case UploadStatus.CustomGame: return "Custom game";
                default: return "Queued";
            }
        }

        /// <summary>Resource key of the status's solid brush (badge text/dot, stat chip dot).</summary>
        public static string BrushKey(UploadStatus status)
        {
            switch (status) {
                case UploadStatus.Success: return "StatusSuccessBrush";
                case UploadStatus.Duplicate: return "StatusDuplicateBrush";
                case UploadStatus.InProgress: return "StatusProgressBrush";
                case UploadStatus.UploadError: return "StatusErrorBrush";
                case UploadStatus.AiDetected: return "StatusAiBrush";
                case UploadStatus.Incomplete: return "StatusIncompleteBrush";
                case UploadStatus.PtrRegion: return "StatusPtrBrush";
                case UploadStatus.TooOld: return "StatusOldBrush";
                default: return "AppTextTertiaryBrush";
            }
        }

        /// <summary>Resource key of the status's ~22%-alpha badge background brush.</summary>
        public static string BgBrushKey(UploadStatus status)
        {
            switch (status) {
                case UploadStatus.Success: return "StatusSuccessBgBrush";
                case UploadStatus.Duplicate: return "StatusDuplicateBgBrush";
                case UploadStatus.InProgress: return "StatusProgressBgBrush";
                case UploadStatus.UploadError: return "StatusErrorBgBrush";
                case UploadStatus.AiDetected: return "StatusAiBgBrush";
                case UploadStatus.Incomplete: return "StatusIncompleteBgBrush";
                case UploadStatus.PtrRegion: return "StatusPtrBgBrush";
                case UploadStatus.TooOld: return "StatusOldBgBrush";
                default: return "AppRowAltBrush";
            }
        }

        /// <summary>The 8 statuses shown in the stats grid, left-to-right, top-to-bottom.</summary>
        public static readonly UploadStatus[] GridOrder = {
            UploadStatus.Success, UploadStatus.Duplicate, UploadStatus.InProgress, UploadStatus.UploadError,
            UploadStatus.AiDetected, UploadStatus.Incomplete, UploadStatus.PtrRegion, UploadStatus.TooOld,
        };
    }
}
