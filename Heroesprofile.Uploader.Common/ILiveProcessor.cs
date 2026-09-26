using Heroes.ReplayParser;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common
{
    public interface ILiveProcessor
    {
        bool PreMatchPage { get; set; }
        Task StartProcessing(string battleLobbyPath);
        Task UpdateData(string stormSavePath);
        TwitchLiveSession Twitch { get; }
    }
}
