using Heroesprofile.Uploader.Common;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

/// <summary>replays_v8.xml, the upload history - in a scratch folder.</summary>
public sealed class ReplayStorageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hp-storage-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string HistoryFile => Path.Combine(_dir, "replays_v8.xml");

    [Fact]
    public void The_replay_id_is_kept_in_the_upload_history()
    {
        var storage = new ReplayStorage(HistoryFile);
        storage.Save(new[] {
            new ReplayFile { Filename = "a.StormReplay", UploadStatus = UploadStatus.Success, ReplayId = 65484996 },
        });

        var loaded = Assert.Single(storage.Load());

        Assert.Equal(65484996, loaded.ReplayId);
        Assert.Equal(UploadStatus.Success, loaded.UploadStatus);
    }

    [Fact]
    public void History_from_before_replay_ids_still_loads()
    {
        File.WriteAllText(HistoryFile, """
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfReplayFile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <ReplayFile>
                <Filename>a.StormReplay</Filename>
                <Created>2026-09-20T21:14:03</Created>
                <Deleted>false</Deleted>
                <UploadStatus>Success</UploadStatus>
              </ReplayFile>
            </ArrayOfReplayFile>
            """);

        var loaded = Assert.Single(new ReplayStorage(HistoryFile).Load());

        Assert.Equal(UploadStatus.Success, loaded.UploadStatus);
        Assert.Equal(0, loaded.ReplayId);
    }
}
