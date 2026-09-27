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
        /// Starts or stops reading ranks: each game's lobby starts a capture, and its first storm save (which
        /// carries the game mode) ends it.
        /// </summary>
        public static void Apply(Manager manager, bool on)
        {
            if (manager == null) {
                return;
            }
            if (!on) {
                RankCapture.RankReader.Stop();
            }
            manager.SetLobbyReader(on ? RankCapture.RankReader.OnLobby : null);
            manager.SetStormSaveReader(on ? RankCapture.RankReader.OnStormSave : null);
        }
#else
        public const bool IsAvailable = false;

        public static void Apply(Manager manager, bool on)
        {
        }
#endif
    }
}
