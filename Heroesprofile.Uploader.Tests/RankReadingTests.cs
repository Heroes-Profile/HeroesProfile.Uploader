using Heroesprofile.Uploader.Desktop;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>
/// The tests build against the normal app, which must never carry the rank reader - only the Ranks build
/// (-p:IncludeRankCapture=true) does. release-desktop.yml also checks the published files.
/// </summary>
public class RankReadingTests
{
    [Fact]
    public void The_normal_build_has_no_rank_reader()
    {
        Assert.False(RankReading.IsAvailable);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Heroesprofile.Uploader.RankCapture");
    }

    [Fact]
    public void Rank_reading_is_off_until_turned_on()
    {
        Assert.False(new AppConfig().ReadRanks);
    }
}
