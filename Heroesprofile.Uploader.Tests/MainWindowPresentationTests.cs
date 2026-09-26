using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop;
using Heroesprofile.Uploader.Desktop.Gui.ViewModels;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>The main window shows replays and counts exactly as the WPF app's converters did.</summary>
public class MainWindowPresentationTests
{
    [Theory]
    [InlineData(UploadStatus.None, "")]
    [InlineData(UploadStatus.Success, "Success")]
    [InlineData(UploadStatus.UploadError, "Upload error")]
    [InlineData(UploadStatus.AiDetected, "Ai detected")]
    [InlineData(UploadStatus.PtrRegion, "Ptr region")]
    [InlineData(UploadStatus.InProgress, "In progress")]
    public void Status_text_is_the_enum_name_split_into_words(UploadStatus status, string expected)
    {
        Assert.Equal(expected, ReplayRowViewModel.FormatStatus(status));
    }

    [Theory]
    [InlineData(UploadStatus.Success, "success")]
    [InlineData(UploadStatus.InProgress, "inProgress")]
    [InlineData(UploadStatus.Duplicate, "neutral")]
    [InlineData(UploadStatus.AiDetected, "neutral")]
    [InlineData(UploadStatus.CustomGame, "neutral")]
    [InlineData(UploadStatus.PtrRegion, "neutral")]
    [InlineData(UploadStatus.TooOld, "neutral")]
    [InlineData(UploadStatus.Brawl, "neutral")]
    [InlineData(UploadStatus.None, "failed")]
    [InlineData(UploadStatus.UploadError, "failed")]
    [InlineData(UploadStatus.Incomplete, "failed")]
    public void Status_colours_follow_the_WPF_buckets(UploadStatus status, string bucket)
    {
        var row = new ReplayRowViewModel(new ReplayFile("2026-09-20 21.14.03 Cursed Hollow.StormReplay") { UploadStatus = status });

        var buckets = new Dictionary<string, bool> {
            ["success"] = row.IsSuccess,
            ["inProgress"] = row.IsInProgress,
            ["neutral"] = row.IsNeutral,
            ["failed"] = row.IsFailed,
        };
        Assert.Equal(bucket, Assert.Single(buckets, b => b.Value).Key);
    }

    [Fact]
    public void Rows_show_just_the_file_name()
    {
        var path = Path.Combine("Accounts", "1", "Replays", "Multiplayer", "2026-09-20 21.14.03 Cursed Hollow.StormReplay");
        Assert.Equal("2026-09-20 21.14.03 Cursed Hollow.StormReplay", new ReplayRowViewModel(new ReplayFile(path)).FileName);
    }

    [Fact]
    public void Count_lines_are_hidden_until_there_is_something_to_count()
    {
        var line = new StatusCountViewModel(UploadStatus.UploadError, "Upload error");
        Assert.False(line.IsVisible);

        line.Count = 3;

        Assert.True(line.IsVisible);
        Assert.Equal("Upload error: 3", line.Text);
    }

    [Fact]
    public void Count_lines_use_the_WPF_order_and_wording()
    {
        Assert.Equal(
            new[] { "Not processed", "Success", "Upload error", "Duplicate", "Ai detected", "Custom game", "Ptr region", "Too old", "Incomplete", "Brawl" },
            StatusCountViewModel.Lines.Select(l => l.Label));
    }

    [Theory]
    [InlineData("Default", AppConfig.LightTheme)]
    [InlineData("MetroDark", AppConfig.DarkTheme)]
    [InlineData("System", AppConfig.SystemTheme)]
    [InlineData("Light", AppConfig.LightTheme)] // written by the Linux-only app
    [InlineData("Dark", AppConfig.DarkTheme)]   // written by the Linux-only app
    [InlineData("nonsense", AppConfig.DarkTheme)]
    public void Theme_values_from_either_app_load(string stored, string expected)
    {
        Assert.Equal(expected, AppConfig.FromJson($$"""{ "Theme": "{{stored}}" }""").Theme);
    }

    [Fact]
    public void New_installs_default_to_the_dark_theme_like_the_WPF_app()
    {
        Assert.Equal(AppConfig.DarkTheme, new AppConfig().Theme);
    }
}
