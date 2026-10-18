using System.Text.Json;
using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// Names and Grandmaster positions read off the samples' cards, and the cards matched to each game's lobby
/// players (from the sample's meta.json, which the uploader took from the lobby file). Skips without the
/// samples.
/// </summary>
public class CardTextSampleTests(ITestOutputHelper output)
{
    private static List<PlayerMatcher.LobbyPlayer> LobbyPlayers(string gameFolder)
    {
        using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(gameFolder, "meta.json")));
        return meta.RootElement.GetProperty("meta").GetProperty("players").EnumerateArray()
            .Select(p => new PlayerMatcher.LobbyPlayer(p.GetProperty("name").GetString() ?? "", p.GetProperty("team").GetInt32()))
            .ToList();
    }

    [Fact]
    public async Task Reads_grandmaster_positions()
    {
        Samples.RequireSamples();
        var grandmasters = Samples.LoadLabels().Where(l => l.Rank.StartsWith("Grandmaster ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(grandmasters);
        foreach (var label in grandmasters) {
            var frame = await Samples.LoadingScreenAsync(label.GameFolder);
            Assert.NotNull(frame);
            var strip = label.Side == Side.Left ? frame.Value.Left : frame.Value.Right;
            var number = await CardText.ReadGrandmasterNumberAsync(strip, label.Side, label.Slot, RankTemplates.Shipped);
            output.WriteLine($"{label.SampleId} {(label.Side == Side.Left ? "L" : "R")}{label.Slot + 1}: labelled {label.Rank}, read {number}");
            Assert.Equal(int.Parse(label.Rank.Split(' ')[1]), number);

            // Without character templates (a digit no sample has shown), Windows reads numbers of two digits
            // or more on its own.
            if (number >= 10) {
                Assert.Equal(number, await CardText.ReadGrandmasterNumberAsync(strip, label.Side, label.Slot, null));
            }
        }
    }

    [Fact]
    public async Task Matches_cards_to_the_lobby_players()
    {
        Samples.RequireSamples();
        var games = Samples.LoadLabels().Select(l => l.GameFolder).Distinct().ToList();
        int cards = 0, confident = 0, latinCards = 0, latinConfident = 0, reordered = 0, flipped = 0, matchedGames = 0, surelyMatched = 0;
        foreach (var game in games) {
            var frame = await Samples.LoadingScreenAsync(game);
            if (frame == null) {
                continue;
            }
            var players = LobbyPlayers(game);
            var names = new List<IReadOnlyList<string>>();
            foreach (var side in new[] { Side.Left, Side.Right }) {
                for (var slot = 0; slot < LoadingScreenReader.Slots; slot++) {
                    names.Add(await CardText.ReadNameAsync(side == Side.Left ? frame.Value.Left : frame.Value.Right, side, slot));
                }
            }
            var matches = PlayerMatcher.MatchCards(names.Select(n => new PlayerMatcher.CardReading(n, Array.Empty<string>())).ToList(), players);
            Assert.NotNull(matches);
            matchedGames++;
            // The lobby order would be: left = team 0 in order, right = team 1 in order.
            if (players[matches[0].Player].Team == 1) {
                flipped++;
            }
            // Only games where every name was read well say anything about the order.
            if (matches.All(m => m.NameConfidence >= 0.7)) {
                surelyMatched++;
                if (!matches.All(m => m.Player % 5 == m.Card % 5)) {
                    reordered++;
                    output.WriteLine($"  reordered: {Path.GetFileName(game)[..8]}: {string.Join(", ", matches.Select(m => players[m.Player].Name))}");
                }
            }
            foreach (var m in matches) {
                var name = players[m.Player].Name;
                var latin = name.All(c => c < 0x250);
                cards++;
                latinCards += latin ? 1 : 0;
                if (m.NameConfidence >= 0.7) {
                    confident++;
                    latinConfident += latin ? 1 : 0;
                } else {
                    output.WriteLine($"  unsure: {Path.GetFileName(game)[..8]} card {m.Card}: read \"{m.NameRead}\", matched {name} ({m.NameConfidence:0.00})");
                }
            }
        }
        output.WriteLine($"{matchedGames} games: {cards} cards, {confident} matched with a name read at least 70% right; " +
            $"Latin-script names {latinConfident}/{latinCards}. Left side was team 1 in {flipped} games; of the {surelyMatched} games with every name read well, {reordered} had cards out of lobby order.");
        Assert.True(latinConfident >= latinCards * 0.95, $"Only {latinConfident} of {latinCards} Latin-script names read well enough");
    }

    /// <summary>The Grandmasters in the samples, by name, and their ladder positions.</summary>
    private static readonly (string Sample, string Player, int Position)[] KnownGrandmasters = {
        ("2f777ca9", "Cieniu", 45), ("bb80fda0", "Cieniu", 45), ("360eb16a", "MutedChat", 30),
        ("371ac48f", "LincoIn", 6), ("371ac48f", "DungoMungo", 2), ("371ac48f", "TheKure", 1),
        ("eca3e189", "LincoIn", 6), ("990adc8a", "LincoIn", 6),
    };

    [Fact]
    public async Task Gives_each_player_their_own_rank()
    {
        Samples.RequireSamples();
        var folders = Samples.LoadLabels().Select(l => l.GameFolder).Distinct().ToList();
        foreach (var (sample, player, position) in KnownGrandmasters) {
            var game = folders.Single(f => Path.GetFileName(f).StartsWith(sample, StringComparison.Ordinal));
            var frames = new List<(Pixels, Pixels)>();
            foreach (var files in Samples.FrameFiles(game).Take(3)) {
                frames.Add(await Samples.DecodeAsync(files));
            }
            var players = LobbyPlayers(game);
            var ranks = await PlayerRanks.ReadAsync(frames, players, RankTemplates.Shipped);
            Assert.NotNull(ranks);
            Assert.Equal(10, ranks.Count);
            var theirs = ranks.Single(r => players[r.Player].Name == player);
            output.WriteLine($"{sample} {player}: {theirs.Rank.Rank} (name {theirs.Match.NameConfidence:0.00})");
            Assert.Equal(League.Grandmaster, theirs.Rank.League);
            Assert.Equal(position, theirs.Rank.LadderPosition);
            // Nobody else in the game is given that position.
            Assert.DoesNotContain(ranks, r => r != theirs && r.Rank.LadderPosition == position && players[r.Player].Name != player);
        }
    }

    /// <summary>
    /// Reads random sample games the way the app does and writes what it found to
    /// rank-samples/reference/check-reads.json, to look over by eye. Explicit: run it by hand.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Write_reads_of_random_games_for_checking()
    {
        Samples.RequireSamples();
        var seed = Environment.TickCount;
        var random = new Random(seed);
        var games = Samples.LoadLabels().Select(l => l.GameFolder).Distinct().OrderBy(_ => random.Next()).Take(8).ToList();
        var results = new List<object>();
        foreach (var game in games) {
            var files = Samples.FrameFiles(game).Take(3).ToList();
            var frames = new List<(Pixels, Pixels)>();
            foreach (var f in files) {
                frames.Add(await Samples.DecodeAsync(f));
            }
            var shown = files.Where((f, i) => LoadingScreenReader.IsLoadingScreen(frames[i].Item1, frames[i].Item2)).Select(f => Path.GetFileName(f.Left)).FirstOrDefault();
            var players = LobbyPlayers(game);
            var ranks = await PlayerRanks.ReadAsync(frames, players, RankTemplates.Shipped);
            results.Add(new {
                game = Path.GetRelativePath(Samples.Folder, game).Replace(Path.DirectorySeparatorChar, '/'),
                frame = shown,
                reads = ranks?.Select(r => new {
                    card = (r.Rank.Side == Side.Left ? "L" : "R") + (r.Rank.Slot + 1),
                    rank = r.Rank.Rank,
                    player = players[r.Player].Name,
                    team = players[r.Player].Team,
                    nameRead = r.Match.NameRead,
                    nameConfidence = Math.Round(r.Match.NameConfidence, 2),
                }),
            });
        }
        var file = Path.Combine(Samples.Folder, "reference", "check-reads.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
        output.WriteLine($"Seed {seed}: wrote {file}");
    }

    /// <summary>
    /// A finished replay's players as the app gets them when the replay is uploaded: parsed by the
    /// uploader's own <see cref="Heroesprofile.Uploader.Common.Analyzer"/>, then the BattleTags filled in
    /// (<see cref="RankReader.FillBattleTags"/>).
    /// </summary>
    private static (Heroes.ReplayParser.Replay Replay, string Fingerprint, List<RankUploader.ReplayPlayer> Players) ParseReplay(string path)
    {
        var file = new Heroesprofile.Uploader.Common.ReplayFile(path);
        var replay = new Heroesprofile.Uploader.Common.Analyzer().Analyze(file);
        Assert.NotNull(replay);
        RankReader.FillBattleTags(replay, path);
        return (replay, file.Fingerprint, replay.Players.Select(p => new RankUploader.ReplayPlayer(p.Name, p.BattleTag, p.BattleNetRegionId, p.Team, p.Character)).ToList());
    }

    /// <summary>
    /// A sample game with its finished replay alongside (rank-samples/reference/replays/a1aab954.StormReplay,
    /// a game of the maintainer's): the replay has the players' heroes, as a storm save does, and what each
    /// card shows (read by eye off the sample).
    /// </summary>
    private static readonly (string Card, string Player, string Hero)[] KnownMatch = {
        ("L1", "Espresso", "Whitemane"), ("L2", "WargHunter", "Muradin"), ("L3", "Dauks", "Anub'arak"),
        ("L4", "davyt", "Artanis"), ("L5", "TheGazMan", "Nazeebo"), ("R1", "Zemill", "Deckard"),
        ("R2", "NavyWolf", "Chen"), ("R3", "JMCH", "Abathur"), ("R4", "HotPotatoMan", "Valla"), ("R5", "Bug690", "Illidan"),
    };

    [Fact]
    public async Task Matches_the_replays_players_by_name_and_hero()
    {
        Samples.RequireSamples();
        var replayFile = Path.Combine(Samples.Folder, "reference", "replays", "a1aab954.StormReplay");
        Assert.SkipUnless(File.Exists(replayFile), "No replay for sample a1aab954 here");
        var game = Samples.LoadLabels().First(l => l.SampleId.StartsWith("a1aab954", StringComparison.Ordinal)).GameFolder;
        var (_, _, replayPlayers) = ParseReplay(replayFile);
        // The finished replay has every player's full BattleTag and hero.
        Assert.All(replayPlayers, p => Assert.True(p.BattleTag > 0 && p.Hero != null, $"{p.Name}#{p.BattleTag} {p.Hero}"));
        var players = replayPlayers.Select(p => new PlayerMatcher.LobbyPlayer(p.Name, p.Team, p.Hero)).ToList();
        output.WriteLine("Players: " + string.Join(", ", replayPlayers.Select(p => $"{p.Name}#{p.BattleTag} ({p.Hero}, team {p.Team})")));

        var frame = (await Samples.LoadingScreenAsync(game))!.Value;
        string Card(PlayerMatcher.Match m) => (m.Card < 5 ? "L" : "R") + (m.Card % 5 + 1);

        // Names and heroes together.
        var ranks = await PlayerRanks.ReadAsync(new[] { frame }, players, RankTemplates.Shipped);
        Assert.NotNull(ranks);
        foreach (var r in ranks) {
            output.WriteLine($"  {Card(r.Match)} -> {players[r.Player].Name} ({players[r.Player].Hero}): name read \"{r.Match.NameRead}\" {r.Match.NameConfidence:0.00}, hero read \"{r.Match.HeroRead}\" {r.Match.HeroConfidence:0.00}");
        }
        foreach (var (card, player, hero) in KnownMatch) {
            var r = ranks.Single(x => players[x.Player].Name == player);
            Assert.Equal(card, Card(r.Match));
            Assert.Equal(hero, players[r.Player].Hero);
        }
        Assert.True(ranks.Count(r => r.Match.HeroConfidence >= 0.7) >= 8, "Too few heroes read");

        // Heroes alone - as when no reader on the PC knows the names' script - still place every card.
        var heroesOnly = new List<PlayerMatcher.CardReading>();
        foreach (var side in new[] { Side.Left, Side.Right }) {
            for (var slot = 0; slot < LoadingScreenReader.Slots; slot++) {
                var strip = side == Side.Left ? frame.Left : frame.Right;
                heroesOnly.Add(new PlayerMatcher.CardReading(Array.Empty<string>(), await CardText.ReadHeroAsync(strip, side, slot)));
            }
        }
        var matches = PlayerMatcher.MatchCards(heroesOnly, players)!;
        foreach (var (card, player, _) in KnownMatch) {
            Assert.Equal(card, Card(matches.Single(m => players[m.Player].Name == player)));
        }
    }

    /// <summary>
    /// Writes how sample a1aab954 was matched to its replay's players - by name and hero, and by hero alone -
    /// to rank-samples/reference/replay-match.json, to draw and look over. Explicit: run it by hand.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Write_the_replay_match_for_checking()
    {
        Samples.RequireSamples();
        var game = Samples.LoadLabels().First(l => l.SampleId.StartsWith("a1aab954", StringComparison.Ordinal)).GameFolder;
        var (_, _, replayPlayers) = ParseReplay(Path.Combine(Samples.Folder, "reference", "replays", "a1aab954.StormReplay"));
        var players = replayPlayers.Select(p => new PlayerMatcher.LobbyPlayer(p.Name, p.Team, p.Hero)).ToList();
        var files = Samples.FrameFiles(game).First();
        var frame = await Samples.DecodeAsync(files);
        var ranks = (await PlayerRanks.ReadAsync(new[] { frame }, players, RankTemplates.Shipped))!;

        var heroesOnly = new List<PlayerMatcher.CardReading>();
        foreach (var side in new[] { Side.Left, Side.Right }) {
            for (var slot = 0; slot < LoadingScreenReader.Slots; slot++) {
                heroesOnly.Add(new PlayerMatcher.CardReading(Array.Empty<string>(), await CardText.ReadHeroAsync(side == Side.Left ? frame.Left : frame.Right, side, slot)));
            }
        }
        var byHero = PlayerMatcher.MatchCards(heroesOnly, players)!;

        var cards = ranks.OrderBy(r => r.Match.Card).Select(r => new {
            card = (r.Match.Card < 5 ? "L" : "R") + (r.Match.Card % 5 + 1),
            rank = r.Rank.Rank,
            player = players[r.Player].Name,
            battletag = $"{replayPlayers[r.Player].Name}#{replayPlayers[r.Player].BattleTag}",
            region = replayPlayers[r.Player].Region,
            replayHero = players[r.Player].Hero,
            nameRead = r.Match.NameRead,
            nameConfidence = Math.Round(r.Match.NameConfidence, 2),
            heroRead = r.Match.HeroRead,
            heroConfidence = Math.Round(r.Match.HeroConfidence, 2),
            heroesOnlyPlayer = players[byHero[r.Match.Card].Player].Name,
        });
        var result = new {
            game = Path.GetRelativePath(Samples.Folder, game).Replace(Path.DirectorySeparatorChar, '/'),
            frame = Path.GetFileName(files.Left),
            lobbyOrder = LobbyPlayers(game).Select(p => p.Name),
            cards,
        };
        await File.WriteAllTextAsync(Path.Combine(Samples.Folder, "reference", "replay-match.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void A_storm_save_gives_its_replays_fingerprint()
    {
        // A storm save and the finished replay of the same game (the maintainer's, 2026-10-18): the reader
        // works the fingerprint out from the one during the match, the uploader from the other afterwards.
        var save = Path.Combine(Samples.Folder, "reference", "replays", "volskaya-2026-10-18.StormSave");
        var replayFile = Path.ChangeExtension(save, ".StormReplay");
        Assert.SkipUnless(File.Exists(save) && File.Exists(replayFile), "No storm save and replay pair here");
        var fromSave = RankReader.Fingerprint(Heroesprofile.Uploader.Common.TwitchLiveSession.ParseLiveFile(save, isFinalReplay: false));
        var (_, fromReplay, _) = ParseReplay(replayFile);
        Assert.NotNull(fromSave);
        Assert.Equal(fromReplay, fromSave);
    }

    [Fact]
    public async Task Cards_read_in_the_match_wait_for_their_replay()
    {
        Samples.RequireSamples();
        var replayFile = Path.Combine(Samples.Folder, "reference", "replays", "a1aab954.StormReplay");
        Assert.SkipUnless(File.Exists(replayFile), "No replay for sample a1aab954 here");
        var game = Samples.LoadLabels().First(l => l.SampleId.StartsWith("a1aab954", StringComparison.Ordinal)).GameFolder;
        var frame = (await Samples.LoadingScreenAsync(game))!.Value;
        var (replay, fingerprint, replayPlayers) = ParseReplay(replayFile);
        var players = replayPlayers.Select(p => new PlayerMatcher.LobbyPlayer(p.Name, p.Team, p.Hero)).ToList();

        // Read during the match: cards only, nobody's identity.
        var cards = (await PlayerRanks.ReadCardsAsync(new[] { frame }, RankTemplates.Shipped))!;
        Assert.Equal(10, cards.Count);

        // With its fingerprint: found by it.
        var pending = new PendingRanks(() => replay.Timestamp.AddMinutes(30));   // "now": just after the game
        pending.Add(new PendingRanks.Game(replay.Timestamp, fingerprint, cards));
        Assert.Null(pending.Take("someone-elses-game", replay.Timestamp.AddDays(1), players));
        var taken = pending.Take(fingerprint, replay.Timestamp, players);
        Assert.NotNull(taken);
        Assert.Equal(0, pending.Count);
        var zemill = taken.Value.Ranks.Single(r => players[r.Player].Name == "Zemill");
        Assert.Equal("Platinum 4", zemill.Rank.Rank);

        // Without one (the storm save couldn't be read): found by its cards fitting the replay's players.
        pending.Add(new PendingRanks.Game(replay.Timestamp.AddMinutes(-3), null, cards));
        taken = pending.Take(fingerprint, replay.Timestamp, players);
        Assert.NotNull(taken);
        Assert.Equal("Platinum 4", taken.Value.Ranks.Single(r => players[r.Player].Name == "Zemill").Rank.Rank);

        // What's sent: full BattleTags, only identified cards.
        var json = System.Text.Json.JsonSerializer.Serialize(RankUploader.Payload(123, replayPlayers, taken.Value.Ranks));
        output.WriteLine(json);
        Assert.Contains("\"battletag\":\"Zemill#1940\"", json);
        Assert.Contains("\"replayID\":123", json);
    }
}
