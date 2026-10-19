using System.Globalization;
using System.Text;
using System.Text.Json;
using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// Reads every sample game that has no labels yet and writes each card's read, its scores against each
/// cutoff, and what the player's own uploader read (meta.json "cards", 3.0.1 on) to
/// rank-samples/reference/survey.csv - for looking new samples over. Explicit:
/// <c>Heroesprofile.Uploader.RankCapture.Tests.exe -method "*Survey_the_unlabelled_games" -explicit only -showliveoutput</c>
/// </summary>
public class SampleSurvey(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public async Task Survey_the_unlabelled_games()
    {
        Samples.RequireSamples();
        var templates = RankTemplates.Shipped!;
        var labelled = Samples.LoadLabels().Select(l => l.GameFolder).ToHashSet();
        var games = Directory.GetDirectories(Samples.Folder, "2*").SelectMany(Directory.GetDirectories)
            .Where(g => !labelled.Contains(g) && File.Exists(Path.Combine(g, "meta.json"))).OrderBy(g => g).ToList();

        var csv = new StringBuilder("game,card,read,client,master,grandmaster,shield,hue,saturation,redOverGreen,blueOverRed,digit,digitScore\n");
        int noLoadingScreen = 0, compared = 0, agreed = 0;
        foreach (var game in games) {
            var frames = new List<(Pixels, Pixels)>();
            foreach (var files in Samples.FrameFiles(game)) {
                var frame = await Samples.DecodeAsync(files);
                if (LoadingScreenReader.IsLoadingScreen(frame.Left, frame.Right)) {
                    frames.Add(frame);
                    if (frames.Count == 3) {
                        break;
                    }
                }
            }
            var id = Path.GetFileName(game)[..8];
            if (frames.Count == 0) {
                noLoadingScreen++;
                csv.Append($"{id},,no loading screen,,,,,,,,,,\n");
                continue;
            }
            var ranks = LoadingScreenReader.ReadGame(frames, templates)!;
            var client = ClientReads(game);
            foreach (var rank in ranks) {
                var strip = rank.Side == Side.Left ? frames[0].Item1 : frames[0].Item2;
                var f = LoadingScreenReader.Measure(strip, rank.Side, rank.Slot, templates);
                var read = rank;
                if (rank.League == League.Grandmaster) {
                    read = rank with { LadderPosition = await CardText.ReadGrandmasterNumberAsync(strip, rank.Side, rank.Slot, templates) };
                }
                var card = $"{(rank.Side == Side.Left ? "L" : "R")}{rank.Slot + 1}";
                var theirs = client.GetValueOrDefault(card, "");
                if (theirs != "") {
                    compared++;
                    agreed += theirs == read.Rank ? 1 : 0;
                }
                string N(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
                csv.Append(string.Join(",", id, card, read.Rank, theirs, N(f.Master), N(f.Grandmaster), N(f.Shield), N(f.Colour.Hue), N(f.Colour.Saturation),
                    N(f.Colour.R - f.Colour.G), N(f.Colour.B - f.Colour.R), f.Digit?.ToString() ?? "", N(f.DigitScore))).Append('\n');
            }
        }
        var path = Path.Combine(Samples.Folder, "reference", "survey.csv");
        await File.WriteAllTextAsync(path, csv.ToString(), TestContext.Current.CancellationToken);
        output.WriteLine($"{games.Count} unlabelled games, {noLoadingScreen} with no loading-screen frame; {agreed}/{compared} cards agree with the player's own uploader. Wrote {path}");
    }

    /// <summary>The ranks the player's own uploader read (meta.json "cards", sent from 3.0.1), by card.</summary>
    private static Dictionary<string, string> ClientReads(string game)
    {
        using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(game, "meta.json")));
        var result = new Dictionary<string, string>();
        if (meta.RootElement.GetProperty("meta").TryGetProperty("cards", out var cards) && cards.ValueKind == JsonValueKind.Array) {
            foreach (var c in cards.EnumerateArray()) {
                result[c.GetProperty("card").GetString()!] = c.GetProperty("rank").GetString() ?? "";
            }
        }
        return result;
    }
}
