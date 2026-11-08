using System.Text;
using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// Builds the golden set (<see cref="Samples.GoldenFolder"/>) from rank-samples/reference/golden-labels.csv
/// (sample folder under rank-samples, card, rank, source): each listed game's first three loading-screen frames
/// and its meta.json, copied into golden/games/, and the labels into golden/labels.csv. The games in
/// reference/golden-fixtures.txt - ones other tests use - are copied whole. Rebuilds from scratch.
/// Explicit, and then sync it up so other machines get it:
/// <c>Heroesprofile.Uploader.RankCapture.Tests.exe -method "*Build_the_golden_set" -explicit only -showliveoutput</c>
/// <c>gcloud storage rsync -r --delete-unmatched-destination-objects rank-samples/golden gs://sl-rank-samples/golden</c>
/// </summary>
public class GoldenSetBuilder(ITestOutputHelper output)
{
    private const int FramesKept = 3;

    [Fact(Explicit = true)]
    public async Task Build_the_golden_set()
    {
        Samples.RequireSamples();
        var source = Path.Combine(Samples.Folder, "reference", "golden-labels.csv");
        var rows = File.ReadLines(source).Skip(1).Select(l => l.Split(',', 4)).Where(p => p.Length == 4).ToList();

        var games = Path.Combine(Samples.GoldenFolder, "games");
        if (Directory.Exists(games)) {
            Directory.Delete(games, recursive: true);
        }
        Directory.CreateDirectory(games);

        var labels = new StringBuilder("sample,card,rank,source\n");
        int copied = 0, leftOut = 0;
        foreach (var game in rows.GroupBy(r => r[0])) {
            var from = Path.Combine(Samples.Folder, game.Key);
            var frames = new List<(string Left, string Right)>();
            if (Directory.Exists(from)) {
                foreach (var files in Samples.FrameFiles(from)) {
                    var frame = await Samples.DecodeAsync(files);
                    if (LoadingScreenReader.IsLoadingScreen(frame.Left, frame.Right)) {
                        frames.Add(files);
                        if (frames.Count == FramesKept) {
                            break;
                        }
                    }
                }
            }
            if (frames.Count == 0) {
                leftOut++;
                output.WriteLine($"{game.Key}: no loading-screen frame here, left out");
                continue;
            }

            var name = Path.GetFileName(game.Key);
            var to = Path.Combine(games, name);
            Directory.CreateDirectory(to);
            foreach (var (left, right) in frames) {
                File.Copy(left, Path.Combine(to, Path.GetFileName(left)));
                File.Copy(right, Path.Combine(to, Path.GetFileName(right)));
            }
            File.Copy(Path.Combine(from, "meta.json"), Path.Combine(to, "meta.json"));
            foreach (var r in game) {
                labels.Append(name).Append(',').Append(r[1]).Append(',').Append(r[2]).Append(',').Append(r[3]).Append('\n');
            }
            copied++;
        }
        await File.WriteAllTextAsync(Samples.GoldenLabelsFile, labels.ToString(), TestContext.Current.CancellationToken);

        var fixtures = File.ReadLines(Path.Combine(Samples.Folder, "reference", "golden-fixtures.txt"))
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        foreach (var fixture in fixtures) {
            var to = Path.Combine(games, Path.GetFileName(fixture));
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(Path.Combine(Samples.Folder, fixture))) {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
            }
        }

        var bytes = Directory.EnumerateFiles(games, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        output.WriteLine($"{copied} labelled games and {fixtures.Count} whole test games copied ({bytes / 1048576.0:0} MB), {leftOut} left out, {rows.Count} labels read. Wrote {Samples.GoldenFolder}");
        Assert.True(copied > 0);
    }
}
