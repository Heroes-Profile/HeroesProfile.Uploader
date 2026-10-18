using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// One game's ranks, in two steps. While the match is on (<see cref="ReadCardsAsync"/>): each card's rank,
    /// voted over every loading-screen frame (<see cref="LoadingScreenReader.ReadGame"/>), a Grandmaster's
    /// ladder position, and the player and hero names as read - nobody's identity yet. Once the finished
    /// replay is in (<see cref="Match"/>): which of its players each card is, by those names
    /// (<see cref="PlayerMatcher"/>). The finished replay decides who played; the files the game writes
    /// during the match don't.
    /// </summary>
    internal static class PlayerRanks
    {
        /// <summary>One card: its rank, and what its player and hero names read as.</summary>
        public sealed record Card(CardRank Rank, PlayerMatcher.CardReading Text);

        /// <summary>A player's rank, and how their card was matched to them.</summary>
        public sealed record PlayerRank(int Player, CardRank Rank, PlayerMatcher.Match Match);

        /// <summary>The ten cards, left side top to bottom then right, or null when no frame shows the loading screen.</summary>
        public static async Task<IReadOnlyList<Card>> ReadCardsAsync(IReadOnlyList<(Pixels Left, Pixels Right)> frames, RankTemplates templates)
        {
            var loading = frames.Where(f => LoadingScreenReader.IsLoadingScreen(f.Left, f.Right)).ToList();
            var ranks = LoadingScreenReader.ReadGame(loading, templates);
            if (ranks == null) {
                return null;
            }

            // Names, heroes and ladder positions from the first loading-screen frame: they don't change
            // between frames, and reading text is the slow part.
            var (left, right) = loading[0];
            var cards = new List<Card>();
            foreach (var rank in ranks) {
                var strip = rank.Side == Side.Left ? left : right;
                var text = new PlayerMatcher.CardReading(
                    await CardText.ReadNameAsync(strip, rank.Side, rank.Slot),
                    await CardText.ReadHeroAsync(strip, rank.Side, rank.Slot));
                var read = rank.League == League.Grandmaster
                    ? rank with { LadderPosition = await CardText.ReadGrandmasterNumberAsync(strip, rank.Side, rank.Slot, templates) }
                    : rank;
                cards.Add(new Card(read, text));
            }
            return cards;
        }

        /// <summary>Each of <paramref name="players"/>' ranks (by index into them), or null if the cards can't be matched.</summary>
        public static IReadOnlyList<PlayerRank> Match(IReadOnlyList<Card> cards, IReadOnlyList<PlayerMatcher.LobbyPlayer> players)
        {
            var matches = PlayerMatcher.MatchCards(cards.Select(c => c.Text).ToList(), players);
            return matches?.Where(m => m.Player >= 0)
                .Select(m => new PlayerRank(m.Player, cards[m.Card].Rank, m))
                .OrderBy(r => r.Player)
                .ToList();
        }

        /// <summary>Both steps at once, for frames and players already in hand.</summary>
        public static async Task<IReadOnlyList<PlayerRank>> ReadAsync(
            IReadOnlyList<(Pixels Left, Pixels Right)> frames, IReadOnlyList<PlayerMatcher.LobbyPlayer> players, RankTemplates templates)
        {
            var cards = await ReadCardsAsync(frames, templates);
            return cards == null ? null : Match(cards, players);
        }
    }
}
