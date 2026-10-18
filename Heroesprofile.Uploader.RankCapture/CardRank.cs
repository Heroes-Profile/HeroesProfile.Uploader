namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>A Storm League rank as the loading screen shows it.</summary>
    public enum League
    {
        /// <summary>The card shows a division badge, but its league couldn't be told.</summary>
        Unknown,
        /// <summary>No division badge: placements not done yet (the frame is just the player's chosen border).</summary>
        Unranked,
        Bronze,
        Silver,
        Gold,
        Platinum,
        Diamond,
        Master,
        Grandmaster,
    }

    /// <summary>Which edge of the loading screen a card is on: the left team's cards are on the left.</summary>
    public enum Side
    {
        Left,
        Right,
    }

    /// <summary>
    /// One player card's rank. <see cref="Slot"/> counts 0-4 from the top. <see cref="Division"/> is the
    /// badge's 1-5 for Bronze to Diamond (null if it couldn't be read, and for every other league).
    /// <see cref="LadderPosition"/> is a Grandmaster's place on the ladder, when it could be read.
    /// </summary>
    public sealed record CardRank(Side Side, int Slot, League League, int? Division)
    {
        public int? LadderPosition { get; init; }

        /// <summary>The rank alone, as the screen shows it: "Gold 3", "Grandmaster #6", "Unranked".</summary>
        public string Rank =>
            $"{League}{(Division is int d ? " " + d : "")}{(LadderPosition is int p ? " #" + p : "")}";

        public override string ToString() => $"{(Side == Side.Left ? "L" : "R")}{Slot + 1} {Rank}";
    }
}
