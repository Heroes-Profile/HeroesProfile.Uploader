using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common.Test
{
    public partial class ManagerTests
    {
        private class MockUploader : IUploader
        {
            public bool UploadToHotslogs { get; set; }

            private Func<ReplayFile, Task> UploadCallback = _ => Task.CompletedTask;
            public void SetUploadCallback(Func<ReplayFile, Task> onUpload)
            {
                var old = UploadCallback;

                UploadCallback = async (ReplayFile file) => {
                    await old(file);
                    await onUpload(file);
                };
            }

            public Task CheckDuplicate(IEnumerable<ReplayFile> replays) => Task.CompletedTask;
            public Task<int> GetMinimumBuild() => Task.FromResult(1);
            public Task Upload(object replay_json, ReplayFile file)
            {
                UploadCallback(file);
                return Task.CompletedTask;
            }
            public async Task<UploadStatus> Upload(object replay_json, string file)
            {
                await Task.Delay(100);
                return UploadStatus.Success;
            }

            public async Task<UploadStatus> Upload(object replay_json, string fingerprint, string file)
            {
                await Task.Delay(100);
                return UploadStatus.Success;
            }
        }
    }
}
