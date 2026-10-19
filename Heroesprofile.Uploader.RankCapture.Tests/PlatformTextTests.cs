using Heroesprofile.Uploader.RankCapture;
using SkiaSharp;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// The pieces each OS does its own way, on made-up images: PNG in and out (every OS), and reading text with
/// whatever reader the OS has - Windows' own, Tesseract on Linux (CI installs it), Vision through the helper
/// on macOS (CI builds it and points HP_RANK_HELPER at it). A reader that isn't there skips its test.
/// </summary>
public class PlatformTextTests
{
    /// <summary>A line of dark text on white, the way CardText hands lines to a reader.</summary>
    private static Pixels Line(string text)
    {
        var info = new SKImageInfo(900, 160, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        using var font = new SKFont(SKTypeface.Default, 72);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        surface.Canvas.DrawText(text, 40, 110, font, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Png.Decode(data.ToArray());
    }

    [Fact]
    public void Png_strips_survive_a_round_trip()
    {
        // 4 x 2 BGRA, every pixel different.
        var bgra = new byte[4 * 2 * 4];
        for (var i = 0; i < 8; i++) {
            bgra[i * 4] = (byte)(i * 10);
            bgra[i * 4 + 1] = (byte)(i * 20);
            bgra[i * 4 + 2] = (byte)(i * 30);
            bgra[i * 4 + 3] = 255;
        }
        var frame = new RawFrame(4, 2, 16, bgra);

        var strip = Png.Decode(Png.EncodeStrip(frame, 1, 2));
        Assert.Equal((2, 2), (strip.Width, strip.Height));
        Assert.Equal((30f, 20f, 10f), (strip.R(0, 0), strip.G(0, 0), strip.B(0, 0)));   // source pixel (1, 0)
        Assert.Equal((180f, 120f, 60f), (strip.R(1, 1), strip.G(1, 1), strip.B(1, 1))); // source pixel (2, 1)
    }

    [Fact]
    public async Task The_systems_text_reader_reads_a_line()
    {
        if (OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("HP_RANK_HELPER") is string helper && File.Exists(helper)) {
            // Tests run outside the app bundle: put the helper where the app would have it.
            File.Copy(helper, MacHelper.FilePath, overwrite: true);
        }
        Assert.SkipWhen(TextReaders.All.Count == 0, "No text reader on this machine");

        var readings = new List<string>();
        foreach (var reader in TextReaders.All) {
            readings.AddRange(await reader.ReadAsync(Line("HeroesProfile 1940")));
        }
        Assert.Contains(readings, r => PlayerMatcher.Similarity(r, "HeroesProfile1940") >= 0.8);
    }

    [Fact]
    public void Only_this_systems_capture_is_chosen()
    {
        var backend = CaptureBackends.Current;
        if (OperatingSystem.IsLinux()) {
            Assert.IsType<X11CaptureBackend>(backend);
        } else if (OperatingSystem.IsMacOS()) {
            Assert.IsType<MacCaptureBackend>(backend);
        }
        Assert.NotNull(backend);
    }
}
