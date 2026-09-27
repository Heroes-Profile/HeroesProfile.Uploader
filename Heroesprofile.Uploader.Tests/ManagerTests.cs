using Heroesprofile.Uploader.Common;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public class ManagerTests
{
    private sealed class NoStorage : IReplayStorage
    {
        public void Save(IEnumerable<ReplayFile> files) { }
        public IEnumerable<ReplayFile> Load() => Array.Empty<ReplayFile>();
    }

    [Fact]
    public void Retry_failed_requeues_only_the_failed_uploads()
    {
        var manager = new Manager(new NoStorage());
        var failed1 = new ReplayFile("a.StormReplay") { UploadStatus = UploadStatus.UploadError };
        var failed2 = new ReplayFile("b.StormReplay") { UploadStatus = UploadStatus.UploadError };
        var uploaded = new ReplayFile("c.StormReplay") { UploadStatus = UploadStatus.Success };
        var duplicate = new ReplayFile("d.StormReplay") { UploadStatus = UploadStatus.Duplicate };
        manager.Files.AddRange(new[] { failed1, failed2, uploaded, duplicate });

        Assert.Equal(2, manager.RetryFailed());

        // Back to "not processed yet", which is what the upload loop picks up.
        Assert.Equal(UploadStatus.None, failed1.UploadStatus);
        Assert.Equal(UploadStatus.None, failed2.UploadStatus);
        Assert.Equal(UploadStatus.Success, uploaded.UploadStatus);
        Assert.Equal(UploadStatus.Duplicate, duplicate.UploadStatus);
    }

    [Fact]
    public void Retry_failed_with_nothing_failed_does_nothing()
    {
        var manager = new Manager(new NoStorage());
        manager.Files.AddRange(new[] { new ReplayFile("a.StormReplay") { UploadStatus = UploadStatus.Success } });

        Assert.Equal(0, manager.RetryFailed());
    }

    [Fact]
    public async Task A_live_file_is_read_only_once_it_stops_growing()
    {
        // Linux/macOS: nothing stops the app reading the battle lobby while the game is still writing it.
        var dir = Directory.CreateTempSubdirectory("hp-settle-");
        try {
            var path = Path.Combine(dir.FullName, "replay.server.battlelobby");
            File.WriteAllBytes(path, new byte[1000]);
            var manager = new Manager(new NoStorage()) { LiveFileSettleTime = TimeSpan.FromMilliseconds(300) };

            var cancel = TestContext.Current.CancellationToken;
            var writer = Task.Run(async () => {
                for (var i = 0; i < 5; i++) {
                    await Task.Delay(100, cancel);
                    using var stream = new FileStream(path, FileMode.Append);
                    stream.Write(new byte[1000]);
                }
            }, cancel);
            await manager.WaitUntilSettled(path);

            Assert.True(writer.IsCompleted);
            Assert.Equal(6000, new FileInfo(path).Length);
        }
        finally {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Without_a_settle_time_live_files_are_read_straight_away()
    {
        // Windows: the game holds the file open while writing, so EnsureFileAvailable already waits.
        var manager = new Manager(new NoStorage());
        var timer = System.Diagnostics.Stopwatch.StartNew();

        await manager.WaitUntilSettled("/nonexistent/replay.server.battlelobby");

        Assert.True(timer.ElapsedMilliseconds < 100);
    }
}
