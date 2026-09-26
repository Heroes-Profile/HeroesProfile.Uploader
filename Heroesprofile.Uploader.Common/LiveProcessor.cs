using Heroes.ReplayParser;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Net;

//Live File Parsers
using MpqBattlelobby = Heroes.ReplayParser.MPQFiles.StandaloneBattleLobbyParser;
using MpqHeader = Heroes.ReplayParser.MPQFiles.MpqHeader;
using MpqDetails = Heroes.ReplayParser.MPQFiles.ReplayDetails;
using MpqAttributeEvents = Heroes.ReplayParser.MPQFiles.ReplayAttributeEvents;
using MpqInitData = Heroes.ReplayParser.MPQFiles.ReplayInitData;
using MpqTrackerEvents = Heroes.ReplayParser.MPQFiles.ReplayTrackerEvents;

namespace Heroesprofile.Uploader.Common
{
    public class LiveProcessor : ILiveProcessor
    {
        public bool PreMatchPage { get; set; }



        private static Logger _log = LogManager.GetCurrentClassLogger();
        HttpClient client = new HttpClient();



        // v1, served from the main site. See the note in Uploader.cs.
#if DEBUG
        private static readonly string heresprofileAPI = @"http://127.0.0.1:8000/api/external/v1/";
        private static readonly string heresprofile = @"http://127.0.0.1:8000/";

#else
        private static readonly string heresprofileAPI = @"https://www.heroesprofile.com/api/external/v1/";
        private static readonly string heresprofile = @"https://www.heroesprofile.com/";

#endif


        private static readonly string preMatchURI = @"PreMatch/Results/?prematchID=";

        private Dictionary<int, int> playerIDTalentIndexDictionary = new Dictionary<int, int>();
        private Dictionary<string, string> foundTalents = new Dictionary<string, string>();

        private Replay replayData;

        /// <summary>The Twitch extension feed. Shared across games; each lobby starts a new one.</summary>
        public TwitchLiveSession Twitch { get; }

        public LiveProcessor(bool PreMatchPage, TwitchLiveSession twitch = null)
        {
            this.PreMatchPage = PreMatchPage;
            Twitch = twitch ?? new TwitchLiveSession();
        }

        public async Task StartProcessing(string battleLobbyPath)
        {
            if (!PreMatchPage && !Twitch.Enabled) {
                return;
            }

            try {
                byte[] replayBytes = File.ReadAllBytes(battleLobbyPath);
                replayData = MpqBattlelobby.Parse(replayBytes);
            }
            catch (Exception ex) {
                _log.Error(ex, $"Failed to read or parse battlelobby '{battleLobbyPath}'");
                return;
            }

            var playerCount = replayData?.Players?.Count(x => x != null) ?? 0;
            if (playerCount == 0) {
                // usually means the game was still writing the file when the watcher fired
                _log.Warn("No players parsed out of the battlelobby, skipping prematch");
                return;
            }

            if (PreMatchPage) {
                await runPreMatch(replayData);
            }

            if (Twitch.Enabled) {
                await Twitch.StartGame(replayData);
            }
        }

        /// <summary>A .StormSave from the game in progress, for the Twitch extension.</summary>
        public Task UpdateData(string stormSavePath)
        {
            return Twitch.UpdateFromStormSave(stormSavePath);
        }


        /// <summary>
        /// Upload replay data to Heroes Profile and open up PreMatch page
        /// </summary>
        private async Task runPreMatch(Replay replayData)
        {
            var apiUrl = $"{heresprofileAPI}prematch";

            try {
                var payload = JsonConvert.SerializeObject(replayData.Players);
                var values = new Dictionary<string, string>
                {
                    { "data", payload },
                };

                var content = new FormUrlEncodedContent(values);

                var response = await client.PostAsync(apiUrl, content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!Int32.TryParse(responseString?.Trim(), out int value)) {
                    _log.Error($"Integer value not returned for prematch replayID. HTTP {(int)response.StatusCode} from {apiUrl}, response string: {Describe(responseString)}");
                    return;
                }

                var pageUrl = $"{heresprofile}{preMatchURI}{value}";
                _log.Debug($"Opening prematch page {pageUrl}");
                try {
                    // UseShellExecute is needed to open a URL rather than try to execute it as a file -
                    // it's the .NET Framework default but not on .NET Core, where it also maps to xdg-open on Linux
                    Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true });
                }
                catch (Exception ex) {
                    _log.Error(ex, $"Failed to open prematch page {pageUrl}");
                }
                WebhookNotifier.Notify("prematch", pageUrl);
            } catch (Exception ex) {
                _log.Error(ex, $"Prematch failed ({apiUrl})");
            }
        }

        /// <summary>
        /// Render a response body for the log: an error page is worth seeing, but not all 40kb of it
        /// </summary>
        private static string Describe(string response)
        {
            if (response == null) {
                return "<null>";
            }
            if (response.Length == 0) {
                return "<empty>";
            }
            var oneLine = response.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length > 500 ? $"{oneLine.Substring(0, 500)}... ({response.Length} chars total)" : oneLine;
        }
    }
}
