using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// The players' loading-screen samples in rank-samples/ (a gitignored mirror of the private sl-rank-samples
/// bucket: one folder per game, its frames' left and right card strips as PNGs) and their labels,
/// rank-samples/reference/labels.csv - one row per card, checked by hand in part (see LabelledSampleTests).
/// The games the tests use are all in the golden set (golden/games), so a machine needs only golden/ and
/// reference/ from the bucket: <see cref="FindGame"/> looks there first, then in the date folders.
/// </summary>
internal static class Samples
{
    /// <summary>The repo root (where the .slnx is).</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Folder => Path.Combine(RepoRoot, "rank-samples");

    public static string LabelsFile => Path.Combine(Folder, "reference", "labels.csv");

    public static bool Available => File.Exists(LabelsFile);

    /// <summary>Skips the calling test when the samples aren't on this machine.</summary>
    public static void RequireSamples()
    {
        if (!Available) {
            Assert.Skip("No loading-screen samples here (rank-samples/ is a local mirror of the private bucket)");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("*.slnx").Any()) {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// One labelled card. <see cref="Rank"/> as written in labels.csv, e.g. "Silver 4", "Master", "Unranked?".
    /// <see cref="OffColours"/>: the capture's colours are off (an HDR display's over-saturated, blown-out
    /// capture - labels.csv says "over-saturated capture"), so its league can't be told from colour.
    /// </summary>
    public sealed record Label(string GameFolder, string SampleId, Side Side, int Slot, string Rank, bool OffColours = false)
    {
        /// <summary>The label's league and division, or null for labels marked unreliable ("Diamond?").</summary>
        public (League League, int? Division)? Expected
        {
            get {
                if (Rank == "Unranked?" || Rank == "Unranked") {
                    return (League.Unranked, null);
                }
                if (Rank.EndsWith('?')) {
                    return null;
                }
                var parts = Rank.Split(' ');
                var league = Enum.Parse<League>(parts[0]);
                // The reader doesn't read the Grandmaster ladder position yet.
                int? division = league is League.Master or League.Grandmaster || parts.Length < 2 ? null : int.Parse(parts[1]);
                return (league, division);
            }
        }
    }

    /// <summary>
    /// A game's folder by the start of its id: its golden copy if there is one, else its date folder; null
    /// when neither is here.
    /// </summary>
    public static string? FindGame(string id)
    {
        var golden = Path.Combine(GoldenFolder, "games");
        var places = Directory.Exists(golden) ? new[] { golden } : Array.Empty<string>();
        return places.Concat(Directory.Exists(Folder) ? Directory.GetDirectories(Folder, "2*") : Array.Empty<string>())
            .SelectMany(d => Directory.GetDirectories(d, id + "*"))
            .FirstOrDefault();
    }

    public static IReadOnlyList<Label> LoadLabels()
    {
        var labels = new List<Label>();
        foreach (var line in File.ReadLines(LabelsFile).Skip(1)) {
            var parts = line.Split(',');
            if (parts.Length < 3) {
                continue;
            }
            // "2026-10-16/520cc08e-*" names the game folder by the start of its id (unique on its own).
            var id = parts[0].Split('/')[1].TrimEnd('*').TrimEnd('-');
            var folder = FindGame(id) ?? throw new DirectoryNotFoundException($"Labelled game {id} isn't here (sync gs://sl-rank-samples/golden)");
            var side = parts[1][0] == 'L' ? Side.Left : Side.Right;
            var offColours = parts.Length > 3 && parts[3].Contains("over-saturated", StringComparison.Ordinal);
            labels.Add(new Label(folder, id, side, parts[1][1] - '1', parts[2], offColours));
        }
        return labels;
    }

    /// <summary>A game's frames in the order the tests try them (frame 1 first if it has one, then the rest in order), as file pairs.</summary>
    public static IEnumerable<(string Left, string Right)> FrameFiles(string gameFolder)
    {
        // The frames a sample has: all 15 in the early ones, a few (0, 7, 14) since 3.0.3 sends only those.
        var indices = Directory.GetFiles(gameFolder, "frame-*-left.png")
            .Select(f => int.TryParse(Path.GetFileName(f).Split('-')[1], out var n) ? n : -1)
            .Where(n => n >= 0)
            .OrderBy(n => n)
            .ToList();
        foreach (var i in indices.Where(n => n == 1).Concat(indices.Where(n => n != 1))) {
            var left = Path.Combine(gameFolder, $"frame-{i}-left.png");
            var right = Path.Combine(gameFolder, $"frame-{i}-right.png");
            if (File.Exists(left) && File.Exists(right)) {
                yield return (left, right);
            }
        }
    }

    public static async Task<(Pixels Left, Pixels Right)> DecodeAsync((string Left, string Right) files) =>
        (await Png.DecodeAsync(await File.ReadAllBytesAsync(files.Left)), await Png.DecodeAsync(await File.ReadAllBytesAsync(files.Right)));

    /// <summary>The first of a game's frames that shows the loading screen, or null.</summary>
    public static async Task<(Pixels Left, Pixels Right)?> LoadingScreenAsync(string gameFolder)
    {
        foreach (var files in FrameFiles(gameFolder)) {
            var frame = await DecodeAsync(files);
            if (LoadingScreenReader.IsLoadingScreen(frame.Left, frame.Right)) {
                return frame;
            }
        }
        return null;
    }

    /// <summary>
    /// The golden set: games picked to cover every league, screen and hard case, each trimmed to its
    /// loading-screen frames, with one label per card (golden/labels.csv: sample, card, rank, source). Built
    /// from reference/golden-labels.csv by <see cref="GoldenSetBuilder"/>, and kept in the bucket's golden/
    /// prefix so a machine can sync just it.
    /// </summary>
    public static string GoldenFolder => Path.Combine(Folder, "golden");

    public static string GoldenLabelsFile => Path.Combine(GoldenFolder, "labels.csv");

    /// <summary>Skips the calling test when the golden set isn't on this machine.</summary>
    public static void RequireGolden()
    {
        if (!File.Exists(GoldenLabelsFile)) {
            Assert.Skip("No golden set here (sync gs://sl-rank-samples/golden to rank-samples/golden)");
        }
    }

    /// <summary>
    /// One golden card: its game's folder name, card ("L1".."R5"), rank as the screen shows it ("Gold 3",
    /// "Grandmaster #17", "Master", "Unranked" for no badge) and where the label came from: "hand-checked ...",
    /// "labels.csv: ..." or "N uploaders agree" (that many uploaders in the same game read it the same).
    /// </summary>
    public sealed record GoldenLabel(string Game, string Card, string Rank, string Source)
    {
        public Side Side => Card[0] == 'L' ? Side.Left : Side.Right;
        public int Slot => Card[1] - '1';
    }

    public static IReadOnlyList<GoldenLabel> LoadGoldenLabels() =>
        File.ReadLines(GoldenLabelsFile).Skip(1)
            .Select(line => line.Split(',', 4))
            .Where(parts => parts.Length == 4)
            .Select(parts => new GoldenLabel(parts[0], parts[1], parts[2], parts[3]))
            .ToList();

    /// <summary>
    /// Whether a game is in the half the templates are built from: its id starts 0-7. The other half (8-f)
    /// stays unseen, to test the reader on.
    /// </summary>
    public static bool IsTemplateGame(string sampleId) => sampleId[0] is >= '0' and <= '7';
}
