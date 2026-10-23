using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// The reader against every labelled card in the samples. The labels came from working the method out on
/// the samples, with these cards checked by hand (<see cref="HandChecked"/>); the templates are built from
/// half the games only (<see cref="Samples.IsTemplateGame"/>), so the other half is new to the reader.
/// Skips without the samples (rank-samples/ is a local mirror of a private bucket).
/// </summary>
public class LabelledSampleTests(ITestOutputHelper output)
{
    /// <summary>Cards the maintainer checked on the loading screen itself (2026-10-16 to 18): sample id, card, rank.</summary>
    private static readonly (string Sample, string Card, string Rank)[] HandChecked = {
        ("09483a38", "L4", "Bronze 2"), ("46e252d5", "L3", "Bronze 4"), ("5173d9fd", "L2", "Bronze 2"), ("7266fe75", "R3", "Bronze 1"),
        ("72d8c528", "R2", "Bronze 1"), ("76e93b8b", "L2", "Bronze 1"), ("d2655f6b", "R3", "Bronze 2"), ("908d1f19", "L2", "Bronze 2"),
        ("78d24114", "L2", "Bronze 2"), ("46e252d5", "R4", "Bronze 2"),
        ("c98a1009", "L2", "Silver 4"), ("17080637", "L5", "Silver 1"), ("24f4fb2a", "L1", "Silver 5"), ("331c8011", "L4", "Silver 3"),
        ("dfd821f2", "L1", "Silver 2"), ("2ac98ea9", "R4", "Silver 3"), ("fb05a0ce", "R1", "Silver 5"), ("c6709e61", "R1", "Silver 1"),
        ("0401e82e", "L1", "Silver 3"), ("753b0e2e", "L3", "Silver 5"), ("e5a974e2", "R2", "Silver 3"), ("63fa816b", "R2", "Silver 5"),
        ("46beaff9", "R4", "Gold 4"), ("a1aab954", "R2", "Gold 5"), ("ac30a263", "R4", "Gold 5"), ("afab46d0", "L5", "Gold 1"),
        ("1da1bdb3", "R3", "Gold 5"), ("79e4f9ff", "L5", "Gold 5"), ("bfa91f4f", "R3", "Gold 3"), ("38d1a640", "L2", "Gold 1"),
        ("c5756568", "L1", "Platinum 5"), ("c43164d6", "L1", "Platinum 2"), ("a933cc95", "R1", "Platinum 5"), ("645d5893", "R1", "Platinum 1"),
        ("e5a974e2", "R3", "Platinum 5"), ("e10b4d34", "L2", "Platinum 1"), ("58ea9c69", "R2", "Platinum 4"), ("704631e2", "R3", "Platinum 2"),
        ("b2a9dcc6", "L5", "Platinum 5"), ("32f5d4e7", "L4", "Platinum 3"),
        ("80a62a16", "L1", "Diamond 2"), ("c925a73e", "L2", "Diamond 4"), ("833e5557", "L4", "Diamond 4"), ("b314a93e", "L5", "Diamond 5"),
        ("b3140787", "R2", "Diamond 5"), ("0549e2ad", "L1", "Diamond 2"), ("c43164d6", "R5", "Diamond 5"), ("e5b423a9", "R5", "Diamond 1"),
        ("1c755e25", "R5", "Diamond 5"), ("07f50c72", "R1", "Diamond 1"),
        ("afab46d0", "L4", "Master"), ("07afa180", "L1", "Master"), ("21fc119f", "L3", "Master"), ("e17d2845", "R1", "Master"),
        ("1d6e6079", "L3", "Master"), ("07afa180", "R2", "Master"), ("eca3e189", "R1", "Master"), ("c925a73e", "L3", "Master"),
        ("a34ae6b0", "L4", "Master"), ("e3e339f3", "R2", "Master"), ("520cc08e", "L4", "Master"), ("360eb16a", "L4", "Master"),
        ("360eb16a", "R1", "Master"), ("360eb16a", "R3", "Master"), ("360eb16a", "R5", "Master"),
        ("eca3e189", "L4", "Grandmaster"), ("360eb16a", "L2", "Grandmaster"), ("990adc8a", "R5", "Grandmaster"), ("53abe0a1", "R3", "Grandmaster"),
        ("bb80fda0", "L4", "Grandmaster"), ("371ac48f", "L4", "Grandmaster"), ("b52335af", "R4", "Grandmaster"), ("2f777ca9", "R5", "Grandmaster"),
        ("88063b97", "R2", "Unranked"), ("c2079602", "L1", "Unranked"), ("57b47163", "R4", "Unranked"), ("92d4c833", "R4", "Unranked"),
        ("38d1a640", "L4", "Unranked"), ("fe2b4921", "R5", "Unranked"), ("24f4fb2a", "R3", "Unranked"), ("18058a78", "R4", "Unranked"),
        ("445b6488", "L1", "Unranked"), ("a639279d", "L3", "Unranked"),
    };

    private static string Describe(League league, int? division) => division is int d ? $"{league} {d}" : league.ToString();

    [Fact]
    public async Task Reads_the_labelled_samples()
    {
        Samples.RequireSamples();
        var templates = RankTemplates.Shipped;
        Assert.NotNull(templates);
        var labels = Samples.LoadLabels().Where(l => l.Expected != null && !l.OffColours).ToList();

        var results = new List<(Samples.Label Label, CardRank? Read)>();
        foreach (var game in labels.GroupBy(l => l.GameFolder)) {
            // Up to three loading-screen frames, voted on, as the app does with all of its.
            var frames = new List<(Pixels, Pixels)>();
            foreach (var files in Samples.FrameFiles(game.Key)) {
                var frame = await Samples.DecodeAsync(files);
                if (LoadingScreenReader.IsLoadingScreen(frame.Left, frame.Right)) {
                    frames.Add(frame);
                    if (frames.Count == 3) {
                        break;
                    }
                }
            }
            var ranks = LoadingScreenReader.ReadGame(frames, templates);
            foreach (var label in game) {
                results.Add((label, ranks?.Single(r => r.Side == label.Side && r.Slot == label.Slot)));
            }
        }

        bool Right((Samples.Label Label, CardRank? Read) r) =>
            r.Read != null && r.Read.League == r.Label.Expected!.Value.League && r.Read.Division == r.Label.Expected.Value.Division;
        bool LeagueRight((Samples.Label Label, CardRank? Read) r) => r.Read != null && r.Read.League == r.Label.Expected!.Value.League;

        var unseen = results.Where(r => !Samples.IsTemplateGame(r.Label.SampleId)).ToList();
        output.WriteLine($"All {results.Count} cards: league {results.Count(LeagueRight)} right, league + division {results.Count(Right)} right");
        output.WriteLine($"Unseen half, {unseen.Count} cards: league {unseen.Count(LeagueRight)} right, league + division {unseen.Count(Right)} right");
        foreach (var league in Enum.GetValues<League>()) {
            var of = results.Where(r => r.Label.Expected!.Value.League == league).ToList();
            if (of.Count > 0) {
                output.WriteLine($"  {league}: {of.Count(Right)}/{of.Count}");
            }
        }
        var wrong = results.Where(r => !Right(r)).ToList();
        foreach (var r in wrong.Take(60)) {
            output.WriteLine($"  wrong: {Path.GetFileName(r.Label.GameFolder)[..8]} {(r.Label.Side == Side.Left ? "L" : "R")}{r.Label.Slot + 1} " +
                $"labelled {Describe(r.Label.Expected!.Value.League, r.Label.Expected.Value.Division)}, read {(r.Read == null ? "nothing" : Describe(r.Read.League, r.Read.Division))}");
        }

        foreach (var (sample, card, rank) in HandChecked) {
            var r = results.Single(x => x.Label.SampleId.StartsWith(sample, StringComparison.Ordinal) && x.Label.Side == (card[0] == 'L' ? Side.Left : Side.Right) && x.Label.Slot == card[1] - '1');
            var parts = rank.Split(' ');
            Assert.True(r.Read != null && r.Read.League == Enum.Parse<League>(parts[0]) && (parts.Length == 1 || r.Read.Division == int.Parse(parts[1])),
                $"Hand-checked {sample} {card} is {rank}, read {(r.Read == null ? "nothing" : Describe(r.Read.League, r.Read.Division))}");
        }
        Assert.True(unseen.Count(Right) >= unseen.Count * 0.99, $"Only {unseen.Count(Right)} of {unseen.Count} unseen cards read right");
    }

    /// <summary>
    /// Captures whose colours are off (one player's HDR display: over-saturated, highlights blown out) can't
    /// have their league told from the frame's colour. They may read Unknown - which is never sent - but
    /// never a wrong league; and every badge must still be found, with its digit.
    /// </summary>
    [Fact]
    public async Task Off_colour_captures_never_read_a_wrong_league()
    {
        Samples.RequireSamples();
        var templates = RankTemplates.Shipped!;
        var labels = Samples.LoadLabels().Where(l => l.OffColours && l.Expected != null).ToList();
        Assert.NotEmpty(labels);
        int unknown = 0;
        foreach (var game in labels.GroupBy(l => l.GameFolder)) {
            var frames = new List<(Pixels, Pixels)>();
            foreach (var files in Samples.FrameFiles(game.Key)) {
                var frame = await Samples.DecodeAsync(files);
                if (LoadingScreenReader.IsLoadingScreen(frame.Left, frame.Right)) {
                    frames.Add(frame);
                    if (frames.Count == 3) {
                        break;
                    }
                }
            }
            var ranks = LoadingScreenReader.ReadGame(frames, templates);
            Assert.NotNull(ranks);
            foreach (var label in game) {
                var read = ranks.Single(r => r.Side == label.Side && r.Slot == label.Slot);
                var (league, division) = label.Expected!.Value;
                var where = $"{label.SampleId} {(label.Side == Side.Left ? "L" : "R")}{label.Slot + 1}";
                output.WriteLine($"{where}: labelled {Describe(league, division)}, read {Describe(read.League, read.Division)}");
                if (league is >= League.Bronze and <= League.Diamond) {
                    Assert.True(read.League == league || read.League == League.Unknown, $"{where} is {league}, read {read.League}");
                    Assert.Equal(division, read.Division);
                    unknown += read.League == League.Unknown ? 1 : 0;
                } else {
                    Assert.Equal(league, read.League);
                }
            }
        }
        output.WriteLine($"{labels.Count} cards, {unknown} read Unknown, none wrong");
    }

    /// <summary>
    /// HDR captures (3.0.2 on, converted back from scRGB), checked by eye on the loading screen (2026-10-23).
    /// The Gold and Platinum ones came out Unknown before their colour ranges were widened; the rest read right
    /// then and must stay right.
    /// </summary>
    private static readonly (string Sample, string Card, string Rank)[] HdrHandChecked = {
        ("0b704977", "R5", "Gold 1"), ("18c10d8f", "R3", "Gold 2"), ("8d7db4af", "L3", "Gold 1"), ("b3bfd86a", "R3", "Gold 4"),
        ("446947fb", "R4", "Gold 3"), ("6a5346b1", "R5", "Gold 1"),
        ("4eb1667b", "R5", "Platinum 3"), ("604c059d", "L5", "Platinum 3"), ("d38af7ad", "L5", "Platinum 4"), ("d38af7ad", "R5", "Platinum 5"),
        ("2315c29a", "L5", "Platinum 2"),
        ("3e31a4b0", "L2", "Platinum 1"), ("4eb1667b", "R4", "Platinum 5"), ("39a99661", "R2", "Bronze 1"), ("b3bfd86a", "L2", "Bronze 1"),
        ("d803418d", "L1", "Diamond 5"), ("023cf38f", "L1", "Gold 5"),
    };

    [Fact]
    public async Task Reads_hdr_captures()
    {
        Samples.RequireSamples();
        var templates = RankTemplates.Shipped!;
        var wrong = new List<string>();
        foreach (var game in HdrHandChecked.GroupBy(c => c.Sample)) {
            var folder = Directory.GetDirectories(Samples.Folder, "2*").SelectMany(d => Directory.GetDirectories(d, game.Key + "*")).FirstOrDefault();
            if (folder == null) {
                Assert.Skip($"HDR sample {game.Key} isn't here (synced 2026-10-23 or later)");
            }
            var frames = new List<(Pixels, Pixels)>();
            foreach (var files in Samples.FrameFiles(folder)) {
                var frame = await Samples.DecodeAsync(files);
                if (LoadingScreenReader.IsLoadingScreen(frame.Left, frame.Right)) {
                    frames.Add(frame);
                }
            }
            var ranks = LoadingScreenReader.ReadGame(frames, templates);
            Assert.NotNull(ranks);
            foreach (var (sample, card, rank) in game) {
                var read = ranks.Single(r => r.Side == (card[0] == 'L' ? Side.Left : Side.Right) && r.Slot == card[1] - '1');
                var said = Describe(read.League, read.Division);
                output.WriteLine($"{sample} {card}: {rank}, read {said}");
                if (said != rank) {
                    wrong.Add($"{sample} {card} is {rank}, read {said}");
                }
            }
        }
        Assert.Empty(wrong);
    }

    /// <summary>
    /// How close the labelled cards come to each cutoff the reader uses - for tuning, so it's explicit:
    /// <c>Heroesprofile.Uploader.RankCapture.Tests.exe -method "*Report_how_close_the_cards_come_to_the_cutoffs" -explicit only -showliveoutput</c>
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Report_how_close_the_cards_come_to_the_cutoffs()
    {
        Samples.RequireSamples();
        var templates = RankTemplates.Shipped!;
        var cards = new List<(League League, LoadingScreenReader.CardFeatures Features)>();
        foreach (var game in Samples.LoadLabels().Where(l => l.Expected != null && !l.OffColours).GroupBy(l => l.GameFolder)) {
            var frame = await Samples.LoadingScreenAsync(game.Key);
            if (frame == null) {
                continue;
            }
            foreach (var label in game) {
                var strip = label.Side == Side.Left ? frame.Value.Left : frame.Value.Right;
                cards.Add((label.Expected!.Value.League, LoadingScreenReader.Measure(strip, label.Side, label.Slot, templates)));
            }
        }

        var top = cards.Where(c => c.League is League.Master or League.Grandmaster).Select(c => Math.Max(c.Features.Master, c.Features.Grandmaster)).ToList();
        var rest = cards.Where(c => c.League is not (League.Master or League.Grandmaster)).Select(c => Math.Max(c.Features.Master, c.Features.Grandmaster)).ToList();
        output.WriteLine($"Master/Grandmaster frame match (cutoff {LoadingScreenReader.MasterCutoff}): theirs lowest {top.Min():0.00}, everyone else's highest {rest.Max():0.00}");
        var gm = cards.Where(c => c.League == League.Grandmaster).Select(c => c.Features.Grandmaster - c.Features.Master).ToList();
        var master = cards.Where(c => c.League == League.Master).Select(c => c.Features.Master - c.Features.Grandmaster).ToList();
        output.WriteLine($"Grandmaster over Master: smallest lead {gm.Min():0.00}; Master over Grandmaster: smallest lead {master.Min():0.00}");

        var badged = cards.Where(c => c.League is >= League.Bronze and <= League.Diamond).Select(c => c.Features.Shield).ToList();
        var unranked = cards.Where(c => c.League == League.Unranked).Select(c => c.Features.Shield).ToList();
        output.WriteLine($"Shield match (cutoff {LoadingScreenReader.ShieldCutoff}): badges lowest {badged.Min():0.00}, unranked highest {unranked.Max():0.00}");

        var digits = cards.Where(c => c.League is >= League.Bronze and <= League.Diamond).Select(c => c.Features.DigitScore).ToList();
        output.WriteLine($"Digit match: lowest best score {digits.Min():0.00}");

        var colours = cards.Where(c => c.League is >= League.Bronze and <= League.Diamond).ToList();
        foreach (var league in new[] { League.Bronze, League.Gold, League.Diamond }) {
            var hues = colours.Where(c => c.League == league).Select(c => c.Features.Colour.Hue).ToList();
            output.WriteLine($"{league} frame hue {hues.Min():0}-{hues.Max():0}, saturation from {colours.Where(c => c.League == league).Min(c => c.Features.Colour.Saturation):0}");
        }
        foreach (var league in new[] { League.Silver, League.Platinum }) {
            var of = colours.Where(c => c.League == league).ToList();
            output.WriteLine($"{league} red-over-green {of.Min(c => c.Features.Colour.R - c.Features.Colour.G):0}..{of.Max(c => c.Features.Colour.R - c.Features.Colour.G):0}, " +
                $"blue-over-red {of.Min(c => c.Features.Colour.B - c.Features.Colour.R):0}..{of.Max(c => c.Features.Colour.B - c.Features.Colour.R):0}, saturation up to {of.Max(c => c.Features.Colour.Saturation):0}");
        }
    }
}
