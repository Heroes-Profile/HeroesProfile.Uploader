using System.Collections.Generic;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common
{
    public interface IUploader
    {
        bool UploadToHotslogs { get; set; }
        Task CheckDuplicate(IEnumerable<ReplayFile> replays);
        Task<int> GetMinimumBuild();
        Task Upload(object replay_json, ReplayFile file);
        Task<UploadStatus> Upload(object replay_json, string fingerprint, string file);
    }
}