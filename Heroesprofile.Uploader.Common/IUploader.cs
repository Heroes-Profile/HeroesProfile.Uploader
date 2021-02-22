using System.Collections.Generic;
using System.Threading.Tasks;
using Heroes.ReplayParser;

namespace Heroesprofile.Uploader.Common
{
    public interface IUploader
    {
        bool UploadToHotslogs { get; set; }
        Task CheckDuplicate(IEnumerable<ReplayFile> replays);
        Task<int> GetMinimumBuild();
        Task Upload(Replay replay_results, ReplayFile file, bool PostMatchPage);
        Task<UploadStatus> Upload(Replay replay_results, string fingerprint, string file, bool PostMatchPage);
    }
}