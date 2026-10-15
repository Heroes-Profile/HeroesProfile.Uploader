using Heroes.ReplayParser;
using NLog;
using System.Linq;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// The Ranks build's rank reader, hooked up through Manager.SetLobbyReader/SetStormSaveReader while "Read
    /// ranks from the loading screen" is on. Per game:
    /// lobby file (the loading screen appears) → capture the game window for its first 15 seconds (in
    /// memory) → first storm save (~80 s into the match; it carries the game mode) → Storm League: send the
    /// loading screen's card strips as samples (beta); any other mode: drop them.
    /// Reading the ranks themselves comes later, built from those samples.
    /// </summary>
    public static class RankReader
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static readonly object _lock = new object();
        private static GameCapture _current;

        /// <summary>
        /// Show Windows' yellow capture outline around the game while capturing (Settings, off by default).
        /// Hidden only where Windows allows it (Windows 11); takes effect from the next game.
        /// </summary>
        public static bool ShowOutline { get; set; }

        /// <summary>A new game's lobby: its players, battletags and teams.</summary>
        public static void OnLobby(Replay lobby)
        {
            var players = lobby?.Players?.Count(p => p != null) ?? 0;
            _log.Info($"Rank reading: new game with {players} players - capturing the loading screen");
            lock (_lock) {
                _current?.Dispose();
                _current = GameCapture.Start(lobby);
            }
        }

        /// <summary>A storm save of the game in progress. Only the first one of each game matters.</summary>
        public static void OnStormSave(Replay save)
        {
            GameCapture game;
            lock (_lock) {
                game = _current;
                _current = null;
            }
            if (game == null) {
                return;
            }

            var frames = game.Finish();
            if (save?.GameMode != GameMode.StormLeague) {
                _log.Info($"Rank reading: {save?.GameMode} game, not Storm League - dropped what was captured");
                return;
            }

            _log.Info($"Rank reading: Storm League game - {frames.Count} loading-screen frames captured");
            Task.Run(() => SampleUploader.UploadAsync(game, frames, save));
        }

        /// <summary>Rank reading was switched off: stop and drop anything in progress.</summary>
        public static void Stop()
        {
            lock (_lock) {
                _current?.Dispose();
                _current = null;
            }
        }
    }
}
