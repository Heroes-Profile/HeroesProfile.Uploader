using Heroes.ReplayParser;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common
{
    public interface ILiveProcessor
    {
        bool PreMatchPage { get; set; }
        bool TwitchExtension { get; set; }

        string hpTwitchAPIKey { get; set; }
        string hpAPIEmail { get; set; }
        string twitchKnickname { get; set; }
        int hpAPIUserID { get; set; }


        Task StartProcessing(string battleLobbyPath);
        Task UpdateData(string stormSavePath);

        Task saveTalentDataTwenty(string stormReplayPath);
    }
}