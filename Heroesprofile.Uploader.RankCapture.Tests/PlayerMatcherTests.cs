using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>Matching cards to lobby players, and reading numbers, on made-up names - these run everywhere.</summary>
public class PlayerMatcherTests
{
    private static readonly PlayerMatcher.LobbyPlayer[] Lobby = {
        new("Espresso", 0), new("WargHunter", 0), new("Dauks", 0), new("davyt", 0), new("TheGazMan", 0),
        new("Bug690", 1), new("Zemill", 1), new("NavyWolf", 1), new("JMCH", 1), new("HotPotatoMan", 1),
    };

    private static string[] Names(IReadOnlyList<PlayerMatcher.Match> matches) => matches.Select(m => Lobby[m.Player].Name).ToArray();

    [Fact]
    public void Cards_out_of_lobby_order_go_to_the_right_players()
    {
        // As a sample showed it: the right side starts with Zemill, Bug690 moved to the bottom - and the
        // reader's usual slips: the Battle.net icon read as a 9, an o read as a 0.
        var read = new[] { "9Esoresso", "WargHunter", "Dauks", "davyt", "TheGazMan", "Zemill", "NavyW01f", "9JMCH", "9HotPotatoMan", "Bug690" };
        var matches = PlayerMatcher.MatchCards(read, Lobby)!;
        Assert.Equal(new[] { "Espresso", "WargHunter", "Dauks", "davyt", "TheGazMan", "Zemill", "NavyWolf", "JMCH", "HotPotatoMan", "Bug690" }, Names(matches));
        Assert.All(matches, m => Assert.True(m.NameConfidence >= 0.7, $"card {m.Card}: {m.NameConfidence}"));
    }

    [Fact]
    public void A_name_that_couldnt_be_read_goes_by_elimination()
    {
        var read = new[] { "Espresso", "", "Dauks", "davyt", "TheGazMan", "Bug690", "Zemill", "NavyWolf", "JMCH", "HotPotatoMan" };
        var matches = PlayerMatcher.MatchCards(read, Lobby)!;
        Assert.Equal("WargHunter", Lobby[matches[1].Player].Name);
        Assert.Equal(0, matches[1].NameConfidence);
        Assert.False(matches[1].Identified);
    }

    [Fact]
    public void Either_team_can_be_on_the_left()
    {
        var read = new[] { "Bug690", "Zemill", "NavyWolf", "JMCH", "HotPotatoMan", "Espresso", "WargHunter", "Dauks", "davyt", "TheGazMan" };
        var matches = PlayerMatcher.MatchCards(read, Lobby)!;
        Assert.Equal(read, Names(matches));
    }

    private static readonly PlayerMatcher.LobbyPlayer[] WithHeroes = {
        new("무서운미더덕", 0, "Chromie"), new("무털", 0, "Falstad"), new("muyaho", 0, "Illidan"), new("GGoma", 0, "Diablo"), new("CUTIESTREET", 0, "Malfurion"),
        new("Haibara", 1, "Muradin"), new("Kuroyuki", 1, "Leoric"), new("Kedera", 1, "Abathur"), new("Knightley", 1, "Medivh"), new("FreeAdd", 1, "Ana"),
    };

    private static PlayerMatcher.CardReading Card(string? name, string? hero) =>
        new(name == null ? Array.Empty<string>() : new[] { name }, hero == null ? Array.Empty<string>() : new[] { hero });

    [Fact]
    public void Heroes_settle_cards_whose_names_couldnt_be_read()
    {
        // Two Korean names on one side that an English-only PC can't read - their heroes tell them apart,
        // even against the lobby order.
        var cards = new[] {
            Card("muyaho", "Illidan"), Card(null, "Falstad"), Card(null, "Chromie"), Card("9GGoma", "Diablo"), Card("CUTIESTREET", "Malfurion"),
            Card("Haibara", "Muradin"), Card("Kuroyuki9", "Leoric"), Card(null, "Abathur"), Card("Knightley", "Medivh"), Card("FreeAdd", "Ana"),
        };
        var matches = PlayerMatcher.MatchCards(cards, WithHeroes)!;
        Assert.Equal(new[] { "muyaho", "무털", "무서운미더덕", "GGoma", "CUTIESTREET", "Haibara", "Kuroyuki", "Kedera", "Knightley", "FreeAdd" },
            matches.Select(m => WithHeroes[m.Player].Name));
        Assert.All(matches, m => Assert.True(m.Identified));
    }

    [Fact]
    public void Of_several_readings_the_one_that_matches_counts()
    {
        // As with an English and a Korean reader both installed: one reads junk, the other the name.
        var cards = WithHeroes.Select(p => new PlayerMatcher.CardReading(new[] { "HaaHA", p.Name }, Array.Empty<string>())).ToList();
        var matches = PlayerMatcher.MatchCards(cards, WithHeroes)!;
        Assert.All(matches, m => {
            Assert.Equal(m.Card, m.Player);
            Assert.Equal(WithHeroes[m.Player].Name, m.NameRead);
            Assert.Equal(1.0, m.NameConfidence, 3);
        });
    }

    [Theory]
    [InlineData("9NoFear", "NoFear", 1.0)]          // icon read before the name
    [InlineData("GreyPowerVan9", "GreyPowerVan", 1.0)] // ...or after it
    [InlineData("soutant", "Soutant", 1.0)]         // case doesn't matter
    [InlineData("", "Edd", 0.0)]                    // nothing read
    public void Similarity_forgives_the_readers_slips(string read, string name, double expected)
    {
        Assert.Equal(expected, PlayerMatcher.Similarity(read, name), 3);
    }

    [Fact]
    public void Similar_isnt_the_same()
    {
        Assert.True(PlayerMatcher.Similarity("Rory", "Eowea") < 0.5);
    }

    [Theory]
    [InlineData("#6", 6)]
    [InlineData("45", 45)]
    [InlineData("45 45 45", 45)]
    [InlineData("#1", 1)]
    [InlineData("", null)]
    [InlineData("C'e", null)]
    [InlineData("0", null)]
    public void Ladder_positions_parse(string text, int? expected)
    {
        Assert.Equal(expected, CardText.ParseNumber(text));
    }
}
