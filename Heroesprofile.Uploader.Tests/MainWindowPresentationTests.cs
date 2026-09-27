using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop;
using Heroesprofile.Uploader.Desktop.Gui.ViewModels;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>The main window shows replays and counts exactly as the WPF app's converters did.</summary>
[Collection(StoredTwitchKey.Collection)]
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

    [Fact]
    public void A_row_links_its_replay_id_to_the_match_page_once_known()
    {
        var file = new ReplayFile("2026-09-20 21.14.03 Cursed Hollow.StormReplay");
        var row = new ReplayRowViewModel(file);
        Assert.False(row.HasReplayId);
        Assert.Null(row.MatchUrl);

        file.ReplayId = 65484996;
        row.RefreshFromFile();

        Assert.True(row.HasReplayId);
        Assert.Equal("65484996", row.ReplayIdText);
        Assert.Equal("https://www.heroesprofile.com/Match/Single/65484996", row.MatchUrl);
    }

    [Fact]
    public void Theme_2_splits_a_replay_name_into_time_and_map()
    {
        var row = new ReplayRowViewModel(new ReplayFile("2026-09-20 21.14.03 Cursed Hollow.StormReplay"));

        Assert.Equal("2026-09-20 21.14.03", row.TimeText);
        Assert.Equal("Cursed Hollow", row.MapText);
    }

    [Fact]
    public void Theme_2_shows_a_renamed_replay_under_its_whole_name()
    {
        var file = new ReplayFile("my best game.StormReplay") { Created = new DateTime(2026, 9, 1, 20, 5, 9) };
        var row = new ReplayRowViewModel(file);

        Assert.Equal("2026-09-01 20.05.09", row.TimeText);
        Assert.Equal("my best game", row.MapText);
    }

    [Theory]
    [InlineData(UploadStatus.None, "Queued")]
    [InlineData(UploadStatus.PtrRegion, "PTR")]
    [InlineData(UploadStatus.AiDetected, "AI detected")]
    [InlineData(UploadStatus.Brawl, "Brawl")]
    public void Theme_2_badges_use_short_labels(UploadStatus status, string expected)
    {
        Assert.Equal(expected, ReplayRowViewModel.ShortLabel(status));
    }

    [Theory]
    [InlineData(null, AppConfig.Theme1Design)]
    [InlineData("", AppConfig.Theme1Design)]
    [InlineData("nonsense", AppConfig.Theme1Design)]
    [InlineData(AppConfig.Theme1Design, AppConfig.Theme1Design)]
    [InlineData(AppConfig.Theme2Design, AppConfig.Theme2Design)]
    public void Design_is_theme_1_unless_theme_2_is_chosen(string? stored, string expected)
    {
        Assert.Equal(expected, new AppConfig { Design = stored }.Design);
        Assert.Equal(AppConfig.Theme1Design, new AppConfig().Design);
    }

    [Fact]
    public void Design_survives_a_save_and_load()
    {
        var json = new AppConfig { Design = AppConfig.Theme2Design }.ToJson();

        Assert.Equal(AppConfig.Theme2Design, AppConfig.FromJson(json).Design);
    }

    [Fact]
    public void Each_design_remembers_its_own_window_size()
    {
        var config = new AppConfig();
        Assert.Equal((700.0, 600.0), config.WindowSizeFor(AppConfig.Theme1Design));
        Assert.Equal((450.0, 600.0), config.WindowSizeFor(AppConfig.Theme2Design));

        config.RememberWindowSize(AppConfig.Theme2Design, 400, 700);

        Assert.Equal((400.0, 700.0), config.WindowSizeFor(AppConfig.Theme2Design));
        Assert.Equal((700.0, 600.0), config.WindowSizeFor(AppConfig.Theme1Design));
    }
}
