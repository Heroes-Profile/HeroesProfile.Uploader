using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using Heroesprofile.Uploader.Common;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// The Ranks build's rank reader, hooked up through Manager.SetLobbyReader/SetStormSaveReader/
    /// SetReplayUploadedReader while "Read ranks from the loading screen" is on. Per game:
    /// lobby file (the loading screen appears) → capture the game window for its first 15 seconds (in
    /// memory) → first storm save (~80 s into the match; it carries the game mode) → Storm League: read the
    /// ten cards (<see cref="PlayerRanks.ReadCardsAsync"/>), keep what was read until the game's replay is up
    /// (<see cref="PendingRanks"/>), and send a few frames as samples (beta); any other mode: drop it all →
    /// the finished replay uploaded (or found already there) → match the cards to its players and send their
    /// ranks with its id (<see cref="RankUploader"/>). The lobby file and storm saves only start and steer
    /// this: who played comes from the finished replay.
    /// </summary>
    public static class RankReader
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static readonly object _lock = new object();
        private static GameCapture _current;
        private static readonly PendingRanks _pending = new PendingRanks();

        /// <summary>
        /// Show Windows' yellow capture outline around the game while capturing (Settings, off by default).
        /// Hidden only where Windows allows it (Windows 11); takes effect from the next game.
        /// </summary>
        public static bool ShowOutline { get; set; }

        /// <summary>A new game's lobby: the loading screen is coming up.</summary>
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
            var mode = save?.GameMode.ToString() ?? "Unknown";
            if (save?.GameMode != GameMode.StormLeague) {
                if (CaptureDump.Enabled) {
                    // Testers' copy (macOS/Linux prereleases): read it anyway, to show what the reader made of it.
                    _log.Info($"Rank reading: {mode} game, not Storm League - nothing sent, a copy kept for testing");
                    Task.Run(async () => CaptureDump.Save(mode, frames, await ReadCardsAsync(frames)));
                } else {
                    _log.Info($"Rank reading: {mode} game, not Storm League - dropped what was captured");
                }
                return;
            }

            _log.Info($"Rank reading: Storm League game - {frames.Count} loading-screen frames captured");
            var fingerprint = Fingerprint(save);
            Task.Run(async () => {
                var reading = await ReadCardsAsync(frames);
                if (reading?.Cards != null) {
                    _pending.Add(new PendingRanks.Game(game.LobbyAt, fingerprint, reading.Cards));
                }
                if (CaptureDump.Enabled) {
                    CaptureDump.Save(mode, frames, reading);
                }
                await SampleUploader.UploadAsync(game, frames, save, reading);
            });
        }

        /// <summary>
        /// A replay is on Heroes Profile (<paramref name="file"/> has its id there). If its cards were read,
        /// match them to its players and send their ranks.
        /// </summary>
        public static void OnReplayUploaded(Replay replay, ReplayFile file)
        {
            if (replay?.GameMode != GameMode.StormLeague || file == null || file.ReplayId <= 0) {
                return;
            }
            FillBattleTags(replay, file.Filename);
            // Players without a BattleTag can't be told apart on the site: they're matched, but not sent.
            var players = (replay.Players ?? Array.Empty<Player>()).Where(p => p != null)
                .Select(p => new RankUploader.ReplayPlayer(p.Name, p.BattleTag, p.BattleNetRegionId, p.Team, p.Character))
                .ToList();
            var taken = _pending.Take(file.Fingerprint, replay.Timestamp,
                players.Select(p => new PlayerMatcher.LobbyPlayer(p.Name, p.Team, p.Hero)).ToList());
            if (taken is not var (_, ranks)) {
                return;
            }

            // A card neither whose name nor hero could be read was placed by elimination: it isn't sent.
            _log.Info($"Rank reading: replay {file.ReplayId}: " + string.Join(", ", ranks.Select(r =>
                $"{players[r.Player].Name}#{players[r.Player].BattleTag} ({players[r.Player].Hero}) {r.Rank.Rank}{(r.Match.Identified ? "" : " (card not identified, not sent)")}")));
            Task.Run(() => RankUploader.SendAsync(file.ReplayId, players, ranks));
        }

        /// <summary>
        /// Fills in the players' BattleTag numbers from the replay file's battle lobby. The uploader parses
        /// replays without it (it comes with the replay's events, which it skips to stay quick), so its
        /// players all have 0 - and the full BattleTag is how Heroes Profile knows them.
        /// </summary>
        internal static void FillBattleTags(Replay replay, string path)
        {
            if (replay?.Players == null || replay.Players.All(p => p == null || p.BattleTag > 0)) {
                return;
            }
            try {
                using var archive = new Foole.Mpq.MpqArchive(path);
                archive.AddListfileFilenames();
                ReplayServerBattlelobby.GetBattleTags(replay, DataParser.GetMpqFile(archive, ReplayServerBattlelobby.FileName));
            }
            catch (Exception ex) {
                _log.Warn(ex, $"Rank reading: couldn't read the BattleTags from {path}");
            }
        }

        /// <summary>
        /// The fingerprint the uploader will give this game's replay (<see cref="Analyzer.GetFingerprint"/>:
        /// the players' Battle.net ids and the game's random seed, both in the storm save too), or null if the
        /// storm save hasn't them.
        /// </summary>
        internal static string Fingerprint(Replay save)
        {
            try {
                if (save?.Players == null || save.Players.Length == 0 || save.RandomValue == 0) {
                    return null;
                }
                return new Analyzer().GetFingerprint(save);
            }
            catch (Exception ex) {
                _log.Debug($"Rank reading: no fingerprint from the storm save: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// What reading a game's frames found: which of them show the loading screen (indexes into the
        /// frames), and the ten cards (null when none could be read).
        /// </summary>
        internal sealed record GameReading(IReadOnlyList<int> LoadingScreenFrames, IReadOnlyList<PlayerRanks.Card> Cards);

        /// <summary>Reads the cards off the kept frames and logs them; null if reading failed.</summary>
        private static async Task<GameReading> ReadCardsAsync(IReadOnlyList<CapturedFrame> frames)
        {
            try {
                var templates = RankTemplates.Shipped;
                if (templates == null) {
                    _log.Warn("Rank reading: this build has no rank templates - can't read ranks");
                    return null;
                }
                var decoded = new List<(Pixels Left, Pixels Right)>();
                foreach (var frame in frames) {
                    decoded.Add((await Png.DecodeAsync(frame.LeftPng), await Png.DecodeAsync(frame.RightPng)));
                }
                var loadingScreen = Enumerable.Range(0, decoded.Count)
                    .Where(i => LoadingScreenReader.IsLoadingScreen(decoded[i].Left, decoded[i].Right))
                    .ToList();
                var cards = await PlayerRanks.ReadCardsAsync(decoded, templates);
                if (cards == null) {
                    _log.Info("Rank reading: none of the frames shows the loading screen - no ranks read");
                } else {
                    _log.Info("Rank reading: read " + string.Join(", ", cards.Select(c =>
                        $"{c.Rank} \"{c.Text.Names.FirstOrDefault()}\"/\"{c.Text.Heroes.FirstOrDefault()}\"")) + " - waiting for the replay");
                }
                return new GameReading(loadingScreen, cards);
            }
            catch (Exception ex) {
                _log.Warn(ex, "Rank reading: couldn't read the ranks");
                return null;
            }
        }

        /// <summary>Rank reading was switched off: stop, and drop anything in progress or waiting.</summary>
        public static void Stop()
        {
            lock (_lock) {
                _current?.Dispose();
                _current = null;
            }
            _pending.Clear();
        }
    }
}
