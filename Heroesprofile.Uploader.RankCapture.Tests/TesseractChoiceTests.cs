using Heroesprofile.Uploader.RankCapture;
using Xunit;
using static Heroesprofile.Uploader.RankCapture.TesseractTextReader;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>
/// Which Tesseract readers Linux uses (<see cref="TesseractTextReader.Choose"/>): the bundled English one,
/// plus the system's for languages the bundle doesn't have. Runs everywhere - no Tesseract needed.
/// </summary>
public class TesseractChoiceTests
{
    private static readonly string[] None = Array.Empty<string>();
    private static readonly string[] Bundled = { "eng" };

    [Fact]
    public void The_bundled_english_reads_when_nothing_is_installed()
    {
        Assert.Equal(new[] { new Choice(Source.Bundled, "eng") }, Choose(Bundled, None));
    }

    [Fact]
    public void A_system_with_only_english_adds_nothing()
    {
        Assert.Equal(new[] { new Choice(Source.Bundled, "eng") }, Choose(Bundled, new[] { "eng" }));
    }

    [Fact]
    public void A_system_language_pack_adds_a_reader_even_with_as_many_languages_as_the_bundle()
    {
        // Only tesseract-data-kor installed: one language, like the bundle - Korean must still be read.
        Assert.Equal(new[] { new Choice(Source.Bundled, "eng"), new Choice(Source.System, "kor") }, Choose(Bundled, new[] { "kor" }));
    }

    [Fact]
    public void English_stays_with_the_bundle_when_the_system_has_none()
    {
        // Russian and Korean but no English: Latin names are still read by the bundled English.
        var choices = Choose(Bundled, new[] { "rus", "kor" });
        Assert.Equal(new[] { new Choice(Source.Bundled, "eng"), new Choice(Source.System, "rus+kor") }, choices);
        Assert.Equal("eng", choices[0].Languages);
    }

    [Fact]
    public void The_system_reads_all_its_languages_at_once_next_to_the_bundle()
    {
        Assert.Equal(new[] { new Choice(Source.Bundled, "eng"), new Choice(Source.System, "eng+kor+rus") },
            Choose(Bundled, new[] { "eng", "kor", "rus" }));
    }

    [Fact]
    public void Without_a_bundle_the_system_reads_as_before()
    {
        // Everything at once, plus English alone (mixing scripts makes it worse at Latin names).
        Assert.Equal(new[] { new Choice(Source.System, "eng+kor"), new Choice(Source.System, "eng") }, Choose(None, new[] { "eng", "kor" }));
        Assert.Equal(new[] { new Choice(Source.System, "eng") }, Choose(None, new[] { "eng" }));
        Assert.Equal(new[] { new Choice(Source.System, "kor") }, Choose(None, new[] { "kor" }));
    }

    [Fact]
    public void No_tesseract_no_readers()
    {
        Assert.Empty(Choose(None, None));
    }
}
