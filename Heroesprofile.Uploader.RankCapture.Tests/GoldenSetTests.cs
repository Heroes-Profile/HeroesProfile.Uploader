using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// The reader against the golden set (<see cref="Samples.GoldenFolder"/>): every labelled card, read the way
/// the app reads a game - a vote over its loading-screen frames, a Grandmaster's ladder position from any frame
/// that shows it. Hand-checked cards must all read right; the rest, labelled by the earlier labels.csv or by
/// uploaders agreeing, 99% or more. Skips without the golden set.
/// </summary>
public class GoldenSetTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Reads_the_golden_set()
    {
        Samples.RequireGolden();
        var templates = RankTemplates.Shipped!;
        var results = new List<(Samples.GoldenLabel Label, string Read)>();
        foreach (var game in Samples.LoadGoldenLabels().GroupBy(l => l.Game)) {
            var folder = Path.Combine(Samples.GoldenFolder, "games", game.Key);
            var loading = new List<(Pixels Left, Pixels Right)>();
            foreach (var files in Samples.FrameFiles(folder)) {
                var frame = await Samples.DecodeAsync(files);
                if (LoadingScreenReader.IsLoadingScreen(frame.Left, frame.Right)) {
                    loading.Add(frame);
                }
            }
            var ranks = LoadingScreenReader.ReadGame(loading, templates);
            foreach (var label in game) {
                var rank = ranks?.Single(r => r.Side == label.Side && r.Slot == label.Slot);
                if (rank?.League == League.Grandmaster) {
                    rank = rank with { LadderPosition = await PlayerRanks.GrandmasterNumberAsync(loading, rank, templates) };
                }
                results.Add((label, rank?.Rank ?? "nothing"));
            }
        }

        var wrong = results.Where(r => !Matches(r.Label.Rank, r.Read)).ToList();
        foreach (var group in results.GroupBy(r => Kind(r.Label.Source))) {
            output.WriteLine($"{group.Key}: {group.Count(r => Matches(r.Label.Rank, r.Read))}/{group.Count()} right");
        }
        foreach (var league in results.GroupBy(r => r.Label.Rank.Split(' ')[0]).OrderBy(g => g.Key)) {
            output.WriteLine($"  {league.Key}: {league.Count(r => Matches(r.Label.Rank, r.Read))}/{league.Count()}");
        }
        foreach (var (label, read) in wrong) {
            output.WriteLine($"  wrong: {label.Game[..8]} {label.Card} labelled {label.Rank} ({label.Source}), read {read}");
        }

        Assert.DoesNotContain(wrong, r => Kind(r.Label.Source) == "hand-checked");
        foreach (var group in results.Where(r => Kind(r.Label.Source) != "hand-checked").GroupBy(r => Kind(r.Label.Source))) {
            var right = group.Count(r => Matches(r.Label.Rank, r.Read));
            Assert.True(right >= group.Count() * 0.99, $"{group.Key}: only {right} of {group.Count()} read right");
        }
    }

    private static string Kind(string source) =>
        source.StartsWith("hand-checked", StringComparison.Ordinal) ? "hand-checked"
        : source.StartsWith("labels.csv", StringComparison.Ordinal) ? "labels.csv"
        : "uploaders agree";

    /// <summary>
    /// Whether a read matches a label. labels.csv writes a ladder position without the "#" ("Grandmaster 6");
    /// a label without one only asks for the league.
    /// </summary>
    private static bool Matches(string label, string read)
    {
        var parts = label.Split(' ');
        if (parts[0] == "Grandmaster") {
            return parts.Length == 1 ? read.StartsWith("Grandmaster", StringComparison.Ordinal) : read == "Grandmaster #" + parts[1].TrimStart('#');
        }
        return read == label;
    }
}
