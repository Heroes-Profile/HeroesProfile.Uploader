using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// Turning an HDR display's scRGB capture back into the game's own colours (<see cref="ScRgb"/>), on made-up
/// pixels - runs everywhere.
/// </summary>
public class ScRgbTests
{
    /// <summary>What an HDR display composes an 8-bit sRGB value as: linear light, SDR white at that many nits.</summary>
    private static float Composed(int srgb, float sdrWhiteNits)
    {
        var c = srgb / 255f;
        var linear = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        return linear * sdrWhiteNits / ScRgb.WhiteNits;
    }

    [Theory]
    [InlineData(80)]   // an SDR display, or HDR with SDR content at scRGB's own white
    [InlineData(200)]  // a typical HDR desktop
    [InlineData(480)]  // SDR content turned right up
    public void Every_colour_the_game_drew_comes_back(float sdrWhiteNits)
    {
        for (var srgb = 0; srgb <= 255; srgb++) {
            // As the screen holds it: a half float.
            var half = (float)(Half)Composed(srgb, sdrWhiteNits);
            var back = ScRgb.ToSrgbByte(half, sdrWhiteNits);
            Assert.True(Math.Abs(back - srgb) <= 1, $"sRGB {srgb} at SDR white {sdrWhiteNits} came back as {back}");
        }
    }

    [Fact]
    public void Brighter_than_sdr_white_is_white_and_below_black_is_black()
    {
        Assert.Equal(255, ScRgb.ToSrgbByte(200 / 80f * 3, 200));
        Assert.Equal(0, ScRgb.ToSrgbByte(-0.25f, 200));
        Assert.Equal(0, ScRgb.ToSrgbByte(float.NaN, 200));
    }

    [Fact]
    public void Without_the_correction_an_hdr_desktop_washes_colours_out()
    {
        // The 8-bit capture's trouble in short: SDR white at 200 nits is scRGB 2.5, so reading the screen as if
        // 1.0 were white lifts the game's mid-grey most of the way to white and clips light grey to white.
        // Corrected, both are what the game drew.
        var midGrey = Composed(128, 200);
        var lightGrey = Composed(200, 200);
        Assert.True(ScRgb.ToSrgbByte(midGrey, ScRgb.WhiteNits) > 180);
        Assert.Equal(255, ScRgb.ToSrgbByte(lightGrey, ScRgb.WhiteNits));
        Assert.InRange(ScRgb.ToSrgbByte(midGrey, 200), 127, 129);
        Assert.InRange(ScRgb.ToSrgbByte(lightGrey, 200), 199, 201);
    }

    [Fact]
    public void Rows_become_bgra_with_the_padding_between_them_skipped()
    {
        // 2 x 2 pixels, each row padded to 24 bytes as Direct3D may map it.
        const int pitch = 24;
        var data = new byte[pitch * 2];
        void Put(int x, int y, float r, float g, float b)
        {
            foreach (var (value, channel) in new[] { (r, 0), (g, 1), (b, 2), (1f, 3) }) {
                BitConverter.TryWriteBytes(data.AsSpan(y * pitch + x * 8 + channel * 2), (Half)value);
            }
        }
        Put(0, 0, 1, 0, 0);
        Put(1, 0, 0, 1, 0);
        Put(0, 1, 0, 0, 1);
        Put(1, 1, 1, 1, 1);

        var bgra = ScRgb.ToBgra(data, 2, 2, pitch, ScRgb.WhiteNits);
        Assert.Equal(new byte[] {
            0, 0, 255, 255,     0, 255, 0, 255,
            255, 0, 0, 255,     255, 255, 255, 255,
        }, bgra);
    }
}
