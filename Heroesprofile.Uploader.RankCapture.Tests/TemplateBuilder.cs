using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// Rebuilds Heroesprofile.Uploader.RankCapture/Templates/rank-templates.bin from the labelled samples. Run it
/// by hand after new samples or labels (it's explicit, so a normal test run skips it), then rebuild:
/// <c>dotnet test --project Heroesprofile.Uploader.RankCapture.Tests -- --filter-method "*Build_templates_from_the_samples" --explicit only</c>
/// </summary>
public class TemplateBuilder(ITestOutputHelper output)
{
    private const int MaxShields = 400;
    private const int MaxPerDigit = 250;

    public static string TemplatesFile => Path.Combine(Samples.RepoRoot, "Heroesprofile.Uploader.RankCapture", "Templates", "rank-templates.bin");

    [Fact(Explicit = true)]
    public async Task Build_templates_from_the_samples()
    {
        Samples.RequireSamples();
        var labels = Samples.LoadLabels().Where(l => l.Expected != null).ToList();

        var masters = new List<float[]>();
        var grandmasters = new List<float[]>();
        var shields = new List<float[]>();
        var digits = Enumerable.Range(0, 5).Select(_ => new List<float[]>()).ToArray();
        var characters = new SortedDictionary<char, List<float[]>>();

        foreach (var game in labels.GroupBy(l => l.GameFolder)) {
            var templateGame = Samples.IsTemplateGame(game.First().SampleId);
            // Grandmasters are rare (11 cards so far), so every one of them goes in.
            if (!templateGame && !game.Any(l => l.Expected!.Value.League == League.Grandmaster)) {
                continue;
            }
            var frame = await Samples.LoadingScreenAsync(game.Key);
            if (frame == null) {
                continue;
            }
            foreach (var label in game) {
                var strip = label.Side == Side.Left ? frame.Value.Left : frame.Value.Right;
                var (league, division) = label.Expected!.Value;
                if (league == League.Grandmaster) {
                    grandmasters.Add(LoadingScreenReader.FrameRing(LoadingScreenReader.Portrait(strip, label.Side, label.Slot)));
                    // "Grandmaster 6" is printed "#6", "Grandmaster 45" as "45": line the shapes up with the
                    // characters, if their count says how.
                    var number = label.Rank.Split(' ')[1];
                    var shapes = CardText.GrandmasterCharacters(strip, label.Side, label.Slot);
                    var text = shapes.Count == number.Length + 1 ? "#" + number : number;
                    if (shapes.Count == text.Length) {
                        for (var i = 0; i < text.Length; i++) {
                            if (!characters.TryGetValue(text[i], out var list)) {
                                characters[text[i]] = list = new List<float[]>();
                            }
                            list.Add(shapes[i].Glyph);
                        }
                    } else {
                        output.WriteLine($"  {label.SampleId} {label.Rank}: {shapes.Count} shapes, left out");
                    }
                }
                if (!templateGame) {
                    continue;
                }
                if (league == League.Master) {
                    masters.Add(LoadingScreenReader.FrameRing(LoadingScreenReader.Portrait(strip, label.Side, label.Slot)));
                }
                if (division is int d) {
                    var badge = LoadingScreenReader.Badge(strip, label.Side, label.Slot);
                    if (shields.Count < MaxShields) {
                        shields.Add(LoadingScreenReader.ShieldEdges(badge));
                    }
                    var glyph = LoadingScreenReader.DigitGlyph(badge);
                    if (glyph != null && digits[d - 1].Count < MaxPerDigit) {
                        digits[d - 1].Add(glyph);
                    }
                }
            }
        }

        output.WriteLine($"Master frames {masters.Count}, Grandmaster frames {grandmasters.Count}, shields {shields.Count}, digits {string.Join("/", digits.Select(d => d.Count))}, " +
            $"Grandmaster characters {string.Join(" ", characters.Select(c => $"{c.Key}x{c.Value.Count}"))}");
        Assert.True(masters.Count >= 5 && grandmasters.Count >= 3 && shields.Count >= 100 && digits.All(d => d.Count >= 20), "Not enough labelled cards to build templates from");

        var templates = new RankTemplates {
            Master = LoadingScreenReader.Normalise(Mean(masters)),
            Grandmaster = LoadingScreenReader.Normalise(Mean(grandmasters)),
            Shield = Mean(shields),
            Digits = digits.Select(Mean).ToArray(),
            GrandmasterCharacters = characters.Keys.ToArray(),
            GrandmasterGlyphs = characters.Values.Select(v => LoadingScreenReader.Normalise(Mean(v))).ToArray(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(TemplatesFile)!);
        using (var file = File.Create(TemplatesFile)) {
            templates.Write(file);
        }
        output.WriteLine("Wrote " + TemplatesFile);
    }

    private static float[] Mean(List<float[]> vectors)
    {
        var mean = new float[vectors[0].Length];
        foreach (var v in vectors) {
            for (var i = 0; i < mean.Length; i++) {
                mean[i] += v[i] / vectors.Count;
            }
        }
        return mean;
    }
}
