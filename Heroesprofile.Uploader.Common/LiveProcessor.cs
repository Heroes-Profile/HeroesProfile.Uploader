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


        private static readonly string heresprofile = @"https://www.heroesprofile.com/";


        private static readonly string preMatchURI = @"PreMatch/Results/?prematchID=";

        private Dictionary<int, int> playerIDTalentIndexDictionary = new Dictionary<int, int>();
        private Dictionary<string, string> foundTalents = new Dictionary<string, string>();

        private Replay replayData;

        public LiveProcessor(bool PreMatchPage)
        {
            this.PreMatchPage = PreMatchPage;
        }

        public async Task StartProcessing(string battleLobbyPath)
        {
            byte[] replayBytes = File.ReadAllBytes(battleLobbyPath);
            replayData = MpqBattlelobby.Parse(replayBytes);

            if (PreMatchPage) {
                await runPreMatch(replayData);
            }
         
        }


        /// <summary>
        /// Upload replay data to Heroes Profile and open up PreMatch page
        /// </summary>
        private async Task runPreMatch(Replay replayData)
        {

            try {

                HttpClient client = new HttpClient();
                var values = new Dictionary<string, string>
                {
                    { "data", JsonConvert.SerializeObject(replayData.Players) },
                };

                var content = new FormUrlEncodedContent(values);

                var response = await client.PostAsync($"{heresprofile}PreMatch/", content);

                var responseString = await response.Content.ReadAsStringAsync();

                if (Int32.TryParse(responseString, out int value)) {
                    Process.Start($"{heresprofile}{preMatchURI}{value}");
                } else {
                    _log.Error($"Integer value not returned for postmatch replayID.  Response string: {responseString}");
                }
            }catch {
                _log.Error($"Prematch failed");
            }
        }
    }
}
