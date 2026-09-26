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
}
