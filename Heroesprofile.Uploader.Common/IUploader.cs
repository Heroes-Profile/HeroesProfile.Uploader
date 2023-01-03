using System.Collections.Generic;
using System.Threading.Tasks;
using Heroes.ReplayParser;

namespace Heroesprofile.Uploader.Common
{
    public interface IUploader
    {
        Task CheckDuplicate(IEnumerable<ReplayFile> replays);
        Task Upload(Replay replay_results, ReplayFile file, bool PostMatchPage);
        Task<UploadStatus> Upload(Replay replay_results, string fingerprint, string file, bool PostMatchPage);
    }
}