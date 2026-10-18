using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Pairs the loading screen's ten cards with the game's ten players. The cards' order isn't the lobby's
    /// (the game moves players about - the reporting player to the top of their side, for one), and either
    /// team can be on either side, so it goes by what's read off the cards: each side is one team, and within
    /// it each card takes the player whose name - and hero, when the players come from a storm save, which has
    /// them - it reads most like. Only ten players to choose from makes that forgiving: a name read half right,
    /// or not at all (a script no reader on the PC knows), is settled by the hero or by the others.
    /// </summary>
    internal static class PlayerMatcher
    {
        /// <summary>
        /// A player: their name, their team (0 or 1), and their hero when known (a storm save has it, the
        /// lobby file doesn't) - as the game client names it, in its language, like the card does.
        /// </summary>
        public sealed record LobbyPlayer(string Name, int Team, string Hero = null);

        /// <summary>What was read off one card: its player name and hero name, one reading per text reader.</summary>
        public sealed record CardReading(IReadOnlyList<string> Names, IReadOnlyList<string> Heroes);

        /// <summary>
        /// A card's player (an index into the players). <see cref="NameConfidence"/> and
        /// <see cref="HeroConfidence"/> (0-1) are how alike the best readings are to the player's name and hero
        /// (0 when nothing was read, or the hero isn't known); both low means the card was given to that player
        /// by elimination.
        /// </summary>
        public sealed record Match(int Card, int Player, double NameConfidence, string NameRead, double HeroConfidence, string HeroRead)
        {
            /// <summary>Whether the card's name or hero was read well enough to say it's that player.</summary>
            public bool Identified => NameConfidence >= 0.7 || HeroConfidence >= 0.7;
        }

        /// <summary>
        /// Which player each card is: <paramref name="cards"/> holds the left cards top to bottom then the right
        /// ones (as <see cref="LoadingScreenReader.ReadFrame"/> orders them).
        /// </summary>
        public static IReadOnlyList<Match> MatchCards(IReadOnlyList<CardReading> cards, IReadOnlyList<LobbyPlayer> players)
        {
            var slots = cards.Count / 2;
            Assignment best = null;
            // Try both ways round: the left side as team 0, and as team 1.
            foreach (var leftTeam in new[] { 0, 1 }) {
                var left = Assign(cards.Take(slots).ToList(), players, leftTeam);
                var right = Assign(cards.Skip(slots).ToList(), players, 1 - leftTeam);
                if (left == null || right == null) {
                    continue;
                }
                var both = new Assignment(left.Players.Concat(right.Players).ToArray(), left.Score + right.Score);
                if (best == null || both.Score > best.Score) {
                    best = both;
                }
            }
            if (best == null) {
                return null;
            }
            return best.Players.Select((p, i) => {
                if (p < 0) {
                    return new Match(i, p, 0, null, 0, null);
                }
                var name = Best(cards[i].Names, players[p].Name);
                var hero = Best(cards[i].Heroes, players[p].Hero);
                return new Match(i, p, name.Score, name.Reading, hero.Score, hero.Reading);
            }).ToList();
        }

        /// <summary>The same, with one name reading per card and no heroes.</summary>
        public static IReadOnlyList<Match> MatchCards(IReadOnlyList<string> cardNames, IReadOnlyList<LobbyPlayer> players) =>
            MatchCards(cardNames.Select(n => new CardReading(new[] { n }, Array.Empty<string>())).ToList(), players);

        /// <summary>The reading most like <paramref name="target"/>, and how alike (0 with no target or readings).</summary>
        private static (double Score, string Reading) Best(IReadOnlyList<string> readings, string target)
        {
            var best = (0.0, readings.FirstOrDefault());
            if (string.IsNullOrEmpty(target)) {
                return best;
            }
            foreach (var reading in readings) {
                var score = Similarity(reading, target);
                if (score > best.Item1) {
                    best = (score, reading);
                }
            }
            return best;
        }

        private sealed record Assignment(int[] Players, double Score);

        // A small pull towards the lobby's own order, which is right more often than not: it decides between
        // cards that nothing could be read off.
        private const double OrderBonus = 0.05;

        /// <summary>The best pairing of one side's cards with one team's players, trying every order.</summary>
        private static Assignment Assign(IReadOnlyList<CardReading> cards, IReadOnlyList<LobbyPlayer> players, int team)
        {
            var candidates = players.Select((p, i) => (p, i)).Where(x => x.p.Team == team).Select(x => x.i).ToList();
            if (candidates.Count == 0) {
                return null;
            }
            var score = new double[cards.Count, candidates.Count];
            for (var c = 0; c < cards.Count; c++) {
                for (var p = 0; p < candidates.Count; p++) {
                    var player = players[candidates[p]];
                    score[c, p] = Best(cards[c].Names, player.Name).Score + Best(cards[c].Heroes, player.Hero).Score + (c == p ? OrderBonus : 0);
                }
            }

            int[] bestOrder = null;
            var bestScore = double.MinValue;
            foreach (var order in Orders(Enumerable.Range(0, candidates.Count).ToList(), cards.Count)) {
                var total = order.Select((p, c) => p < 0 ? 0 : score[c, p]).Sum();
                if (total > bestScore) {
                    bestScore = total;
                    bestOrder = order;
                }
            }
            return new Assignment(bestOrder.Select(p => p < 0 ? -1 : candidates[p]).ToArray(), bestScore);
        }

        /// <summary>Every way to give <paramref name="count"/> cards distinct players (-1 for none once they run out).</summary>
        private static IEnumerable<int[]> Orders(List<int> left, int count)
        {
            if (count == 0) {
                yield return Array.Empty<int>();
                yield break;
            }
            if (left.Count == 0) {
                foreach (var rest in Orders(left, count - 1)) {
                    yield return new[] { -1 }.Concat(rest).ToArray();
                }
                yield break;
            }
            foreach (var p in left) {
                var others = left.Where(x => x != p).ToList();
                foreach (var rest in Orders(others, count - 1)) {
                    yield return new[] { p }.Concat(rest).ToArray();
                }
            }
        }

        // The party / Battle.net icon beside a name gets read as a stray character or two ("9NoFear", "Player9").
        private const int MaxStray = 3;

        /// <summary>
        /// How alike a read name and a lobby name are, 0-1: one minus their edit distance over the longer one's
        /// length, ignoring case, spaces and punctuation - and up to <see cref="MaxStray"/> stray characters
        /// read at either end of the name. Nothing read scores 0.
        /// </summary>
        public static double Similarity(string read, string name)
        {
            var a = Simplify(read);
            var b = Simplify(name);
            if (a.Length == 0 || b.Length == 0) {
                return 0;
            }
            var best = 0.0;
            for (var front = 0; front <= MaxStray; front++) {
                for (var back = 0; back <= MaxStray && front + back < a.Length; back++) {
                    var trimmed = a.Substring(front, a.Length - front - back);
                    best = Math.Max(best, 1 - (double)EditDistance(trimmed, b) / Math.Max(trimmed.Length, b.Length));
                }
            }
            return best;
        }

        private static string Simplify(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in (s ?? "").ToLowerInvariant()) {
                if (char.IsLetterOrDigit(c)) {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static int EditDistance(string a, string b)
        {
            var previous = Enumerable.Range(0, b.Length + 1).ToArray();
            var current = new int[b.Length + 1];
            for (var i = 1; i <= a.Length; i++) {
                current[0] = i;
                for (var j = 1; j <= b.Length; j++) {
                    var substitute = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    current[j] = Math.Min(substitute, Math.Min(previous[j] + 1, current[j - 1] + 1));
                }
                (previous, current) = (current, previous);
            }
            return previous[b.Length];
        }
    }
}
