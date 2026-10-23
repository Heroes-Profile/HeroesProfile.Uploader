using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>The reader's pieces on made-up pixels - these run everywhere, samples or not.</summary>
public class LoadingScreenReaderTests
{
    private static Pixels Solid(int width, int height, float r, float g, float b)
    {
        var pixels = new Pixels(width, height);
        for (var i = 0; i < width * height; i++) {
            pixels.Rgb[i * 3] = r;
            pixels.Rgb[i * 3 + 1] = g;
            pixels.Rgb[i * 3 + 2] = b;
        }
        return pixels;
    }

    [Fact]
    public void Shrinking_averages_the_pixels_each_output_pixel_covers()
    {
        // 4 x 2: left half 0, right half 100.
        var image = new Pixels(4, 2);
        for (var y = 0; y < 2; y++) {
            for (var x = 2; x < 4; x++) {
                image.Rgb[(y * 4 + x) * 3] = 100;
            }
        }
        var half = image.Sample(0, 0, 2, 2, 1);
        Assert.Equal(0, half.R(0, 0), 3);
        Assert.Equal(100, half.R(1, 0), 3);

        // Straddling the edge: half of each.
        var straddle = image.Sample(1, 0, 2, 1, 1);
        Assert.Equal(50, straddle.R(0, 0), 3);
    }

    [Fact]
    public void Enlarging_interpolates_between_neighbours()
    {
        var image = new Pixels(2, 1);
        image.Rgb[3] = 100;
        var big = image.Sample(0, 0, 0.5, 4, 1);
        Assert.Equal(new[] { 0f, 25f, 75f, 100f }, Enumerable.Range(0, 4).Select(x => big.R(x, 0)));
    }

    [Fact]
    public void Bgra_rows_become_rgb()
    {
        var pixels = Pixels.FromBgra(new byte[] { 10, 20, 30, 255, 0, 0, 0, 0 }, 1, 1, 8);
        Assert.Equal((30f, 20f, 10f), (pixels.R(0, 0), pixels.G(0, 0), pixels.B(0, 0)));
    }

    [Fact]
    public void Hsv_matches_the_usual_definitions()
    {
        var (hue, saturation, value) = Solid(1, 1, 255, 128, 0).Hsv(0, 0);
        Assert.Equal(30, hue, 0);
        Assert.Equal(255, saturation, 1);
        Assert.Equal(255, value, 1);
        Assert.Equal(0, Solid(1, 1, 90, 90, 90).Hsv(0, 0).Saturation);
    }

    [Theory]
    [InlineData(180, 100, 60, League.Bronze)]     // copper
    [InlineData(200, 160, 60, League.Gold)]       // yellow gold
    [InlineData(60, 180, 230, League.Diamond)]    // cyan
    [InlineData(150, 160, 175, League.Silver)]    // neutral, slightly blue grey
    [InlineData(190, 165, 185, League.Platinum)]  // pink-tinted silver
    [InlineData(200, 195, 96, League.Gold)]       // HDR gold, converted back from scRGB: yellower (hue 57)
    [InlineData(180, 169, 182, League.Platinum)]  // HDR platinum: less pink (red over green 11)
    [InlineData(150, 90, 200, League.Unknown)]    // a purple cosmetic border
    public void Frame_colours_map_to_leagues(float r, float g, float b, League league)
    {
        var portrait = Solid(132, 132, r, g, b);
        Assert.Equal(league, LoadingScreenReader.LeagueFromColour(LoadingScreenReader.MeasureFrameColour(portrait)));
    }

    [Fact]
    public void A_frame_without_cards_isnt_the_loading_screen()
    {
        Assert.False(LoadingScreenReader.IsLoadingScreen(Solid(640, 1440, 0, 0, 0), Solid(640, 1440, 0, 0, 0)));
        Assert.False(LoadingScreenReader.IsLoadingScreen(Solid(40, 28, 200, 50, 50), Solid(40, 28, 200, 50, 50)));
    }

    private static IReadOnlyList<CardRank> OneCard(League league, int? division) => new[] { new CardRank(Side.Left, 0, league, division) };

    [Fact]
    public void Frames_vote_and_a_rank_needs_two_of_them()
    {
        var vote = LoadingScreenReader.Vote(new[] { OneCard(League.Gold, 3), OneCard(League.Gold, 3), OneCard(League.Gold, 2) });
        Assert.Equal(new CardRank(Side.Left, 0, League.Gold, 3), vote![0]);

        // One frame alone saying ranked isn't enough when others disagree...
        vote = LoadingScreenReader.Vote(new[] { OneCard(League.Unranked, null), OneCard(League.Unranked, null), OneCard(League.Silver, 1) });
        Assert.Equal(League.Unranked, vote![0].League);

        // ...but a single frame is taken as it is.
        Assert.Equal(League.Silver, LoadingScreenReader.Vote(new[] { OneCard(League.Silver, 1) })![0].League);
        Assert.Null(LoadingScreenReader.Vote(Array.Empty<IReadOnlyList<CardRank>>()));
    }

    [Fact]
    public void Ranks_read_like_the_screen()
    {
        Assert.Equal("L1 Gold 3", new CardRank(Side.Left, 0, League.Gold, 3).ToString());
        Assert.Equal("R5 Master", new CardRank(Side.Right, 4, League.Master, null).ToString());
        Assert.Equal("Grandmaster #6", new CardRank(Side.Left, 2, League.Grandmaster, null) { LadderPosition = 6 }.Rank);
    }

    [Fact]
    public void Templates_survive_a_round_trip()
    {
        var templates = new RankTemplates {
            Master = new[] { 1f, 2f },
            Grandmaster = new[] { 3f },
            Shield = new[] { 4f, 5f, 6f },
            Digits = new[] { new[] { 7f }, new[] { 8f, 9f } },
            GrandmasterCharacters = new[] { '#', '4' },
            GrandmasterGlyphs = new[] { new[] { 10f }, new[] { 11f, 12f } },
        };
        using var stream = new MemoryStream();
        templates.Write(new NonClosing(stream));
        stream.Position = 0;
        var back = RankTemplates.Read(stream);
        Assert.Equal(templates.Master, back.Master);
        Assert.Equal(templates.Grandmaster, back.Grandmaster);
        Assert.Equal(templates.Shield, back.Shield);
        Assert.Equal(templates.Digits, back.Digits);
        Assert.Equal(templates.GrandmasterCharacters, back.GrandmasterCharacters);
        Assert.Equal(templates.GrandmasterGlyphs, back.GrandmasterGlyphs);
    }

    [Fact]
    public void The_reader_ships_with_its_templates()
    {
        var templates = RankTemplates.Shipped;
        Assert.NotNull(templates);
        Assert.Equal(5, templates.Digits.Length);
        Assert.All(templates.Digits, d => Assert.Equal(LoadingScreenReader.DigitWidth * LoadingScreenReader.DigitHeight, d.Length));
    }

    /// <summary>Keeps BinaryWriter from closing the stream under the test.</summary>
    private sealed class NonClosing(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
