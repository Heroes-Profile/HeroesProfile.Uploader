using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Heroes.ReplayParser;

namespace Heroesprofile.Uploader.Common.Test
{
    public partial class ManagerTests
    {
        private class MockUploader : IUploader
        {
            public bool UploadToHotslogs { get; set; }
            public bool PostMatchPage { get; set; }

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
            public Task Upload(Replay replay_results, ReplayFile file, bool PostMatchPag)
            {
                UploadCallback(file);
                return Task.CompletedTask;
            }
            public async Task<UploadStatus> Upload(Replay replay_results, string file, bool PostMatchPage)
            {
                await Task.Delay(100);
                return UploadStatus.Success;
            }

            public async Task<UploadStatus> Upload(Replay replay_results, string fingerprint, string file, bool PostMatchPag)
            {
                await Task.Delay(100);
                return UploadStatus.Success;
            }
        }
    }
}
