using Heroesprofile.Uploader.Common;

namespace Heroesprofile.Uploader.Desktop
{
    /// <summary>
    /// The only place the app touches the opt-in rank reader, which only the "Ranks" build contains
    /// (built with -p:IncludeRankCapture=true - see the Desktop .csproj). In the normal build this is all
    /// there is: <see cref="IsAvailable"/> is false and nothing is ever hooked up.
    /// </summary>
    internal static class RankReading
    {
#if RANK_CAPTURE
        public const bool IsAvailable = true;

        /// <summary>
        /// Starts or stops reading ranks: each game's lobby starts a capture, its first storm save (which
        /// carries the game mode) ends it, and its replay reaching Heroes Profile sends the ranks read.
        /// </summary>
        public static void Apply(Manager manager, bool on)
        {
            if (manager == null) {
                return;
            }
            if (on) {
                manager.SetReaders(RankCapture.RankReader.OnLobby, RankCapture.RankReader.OnStormSave, RankCapture.RankReader.OnReplayUploaded);
            } else {
                RankCapture.RankReader.Stop();
                manager.SetReaders(null, null, null);
            }
        }

        /// <summary>Show Windows' capture outline around the game (from the next game on).</summary>
        public static void SetShowOutline(bool show) => RankCapture.RankReader.ShowOutline = show;
#else
        public const bool IsAvailable = false;

        public static void Apply(Manager manager, bool on)
        {
        }

        public static void SetShowOutline(bool show)
        {
        }
#endif
    }
}
