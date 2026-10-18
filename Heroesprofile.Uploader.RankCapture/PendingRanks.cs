using System;
using System.Collections.Generic;
using System.Linq;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Cards read during a match, waiting for that match's finished replay to be on Heroes Profile: only then
    /// are they matched to players (the replay's, the source of truth) and sent with the replay's id. A game
    /// is found by its fingerprint - the same one the uploader gives the replay, worked out from the first
    /// storm save - or, when the storm save couldn't be read, by its cards fitting the replay's players.
    /// Held in memory: a game whose replay doesn't come within <see cref="KeepFor"/> (not finished, uploading
    /// off, the app closed) is dropped.
    /// </summary>
    internal sealed class PendingRanks
    {
        /// <summary>A match's cards: when its loading screen was read, and its fingerprint if known.</summary>
        public sealed record Game(DateTime ReadAt, string Fingerprint, IReadOnlyList<PlayerRanks.Card> Cards);

        public static readonly TimeSpan KeepFor = TimeSpan.FromHours(6);

        // A replay found by its players rather than its fingerprint must have been played around when its
        // cards were read, and fit them: at least this many cards identified (by name or hero).
        private static readonly TimeSpan FitWindow = TimeSpan.FromHours(2);
        public const int FitMinimumIdentified = 8;

        private readonly object _lock = new object();
        private readonly List<Game> _games = new List<Game>();
        private readonly Func<DateTime> _now;

        public PendingRanks(Func<DateTime> now = null)
        {
            _now = now ?? (() => DateTime.UtcNow);
        }

        public int Count
        {
            get {
                lock (_lock) {
                    Expire();
                    return _games.Count;
                }
            }
        }

        public void Add(Game game)
        {
            lock (_lock) {
                Expire();
                _games.Add(game);
            }
        }

        /// <summary>
        /// The waiting game that a finished replay is, taken out of the store with its players' ranks - or
        /// null if none is. <paramref name="fingerprint"/> is the replay's; <paramref name="gameStart"/> when
        /// it was played (UTC).
        /// </summary>
        public (Game Game, IReadOnlyList<PlayerRanks.PlayerRank> Ranks)? Take(string fingerprint, DateTime gameStart, IReadOnlyList<PlayerMatcher.LobbyPlayer> players)
        {
            lock (_lock) {
                Expire();
                var game = fingerprint == null ? null : _games.FirstOrDefault(g => g.Fingerprint == fingerprint);
                IReadOnlyList<PlayerRanks.PlayerRank> ranks = null;
                if (game != null) {
                    ranks = PlayerRanks.Match(game.Cards, players);
                } else {
                    // No fingerprint to go by: the game, read around then, whose cards fit these players best.
                    foreach (var candidate in _games.Where(g => g.Fingerprint == null && (g.ReadAt - gameStart).Duration() <= FitWindow)) {
                        var matched = PlayerRanks.Match(candidate.Cards, players);
                        var identified = matched?.Count(r => r.Match.Identified) ?? 0;
                        if (identified >= FitMinimumIdentified && identified > (ranks?.Count(r => r.Match.Identified) ?? 0)) {
                            game = candidate;
                            ranks = matched;
                        }
                    }
                }
                if (game == null || ranks == null) {
                    return null;
                }
                _games.Remove(game);
                return (game, ranks);
            }
        }

        /// <summary>Drops every waiting game.</summary>
        public void Clear()
        {
            lock (_lock) {
                _games.Clear();
            }
        }

        private void Expire()
        {
            var cutoff = _now() - KeepFor;
            _games.RemoveAll(g => g.ReadAt < cutoff);
        }
    }
}
