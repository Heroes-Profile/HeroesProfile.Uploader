using Heroesprofile.Uploader.RankCapture;
using Xunit;

namespace Heroesprofile.Uploader.RankCapture.Tests;

/// <summary>Which captured frames go to Heroes Profile as samples.</summary>
public class SampleUploaderTests
{
    [Fact]
    public void A_full_loading_screen_sends_its_first_middle_and_last_frames()
    {
        Assert.Equal(new[] { 0, 7, 14 }, SampleUploader.FramesToSend(15, Enumerable.Range(0, 15).ToList()));
        Assert.Equal(new[] { 2, 6, 10 }, SampleUploader.FramesToSend(15, new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
    }

    [Fact]
    public void A_short_loading_screen_sends_every_frame_of_it()
    {
        Assert.Equal(new[] { 4, 5 }, SampleUploader.FramesToSend(15, new[] { 4, 5 }));
    }

    [Fact]
    public void Without_the_loading_screen_one_frame_shows_what_was_caught()
    {
        Assert.Equal(new[] { 7 }, SampleUploader.FramesToSend(15, Array.Empty<int>()));
        Assert.Equal(new[] { 1 }, SampleUploader.FramesToSend(2, null!));
        Assert.Empty(SampleUploader.FramesToSend(0, null!));
    }
}
