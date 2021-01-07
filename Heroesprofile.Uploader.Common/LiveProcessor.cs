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
        public bool TwitchExtension { get; set; }

        public string hpTwitchAPIKey { get; set; }
        public string hpAPIEmail { get; set; }
        public string twitchKnickname { get; set; }
        public int hpAPIUserID { get; set; }

        private static Logger _log = LogManager.GetCurrentClassLogger();
        HttpClient client = new HttpClient();


        private static readonly string heresprofile = @"https://www.heroesprofile.com/";
        private static readonly string heresprofileAPI = @"https://api.heroesprofile.com/";


        private static readonly string preMatchURI = @"PreMatch/Results/?prematchID=";

        private static readonly string saveReplayUrl = @"twitch/extension/save/replay";
        private static readonly string savePlayersUrl = @"twitch/extension/save/player";
        private static readonly string saveTalentsUrl = @"twitch/extension/save/talent";

        private bool start_new_parse = true;
        private int latest_replayID = 0;
        private bool preMatchRan = false;
        private int latest_trackever_event = 0;
        private Dictionary<int, int> playerIDTalentIndexDictionary = new Dictionary<int, int>();

        public async Task Start(string stormSavePath)
        {
            if (PreMatchPage && !preMatchRan) {
                await runPreMatch(stormSavePath);
                preMatchRan = true;
            }

            if (TwitchExtension) {
                await runTwitchExtension(stormSavePath);
            }

        }


        /// <summary>
        /// Upload replay data to Heroes Profile and open up PreMatch page
        /// </summary>
        private async Task runPreMatch(string stormSavePath)
        {
            byte[] replayBytes = File.ReadAllBytes(stormSavePath);
            Replay replayData = MpqBattlelobby.Parse(replayBytes);

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
        }




        private async Task runTwitchExtension(string stormSavePath)
        {

            var replay = new Replay();
            MpqHeader.ParseHeader(replay, stormSavePath);

            using (var archive = new Foole.Mpq.MpqArchive(stormSavePath)) {
                archive.AddListfileFilenames();


                MpqDetails.Parse(replay, DataParser.GetMpqFile(archive, "save.details"), true);

                if (archive.FileExists("replay.attributes.events")) {
                    MpqAttributeEvents.Parse(replay, DataParser.GetMpqFile(archive, "replay.attributes.events"));
                }

                //Get Game Mode               
                if (archive.FileExists("save.initData")) {
                    MpqInitData.Parse(replay, DataParser.GetMpqFile(archive, "save.initData"));
                }


                //Uneeded
                //if (archive.FileExists("replay.game.events")) {
                //    MpqGameEvents.Parse(DataParser.GetMpqFile(archive, "replay.game.events"), replay.Players, replay.ReplayBuild, replay.ReplayVersionMajor, false);
                //}

                //Uneeded
                //if (archive.FileExists("replay.message.events")) {
                //    MpqMessageEvents.Parse(replay, DataParser.GetMpqFile(archive, "replay.message.events"));
                //}

                //Fails
                //if (archive.FileExists("replay.resumable.events")) {
                //    MpqResumableEvents.Parse(replay, DataParser.GetMpqFile(archive, "replay.resumable.events"));
                //}


                //Uneeded

                //for (int i = 0; i < replay.Players.Length; i++)
                //{
                //    replay.Players[i].Talents = new Talent[7];
                //    for (int j = 0; j < replay.Players[i].Talents.Length; j++)
                //    {
                //        replay.Players[i].Talents[j] = new Talent();
                //    }
                //}


                if (archive.FileExists("replay.tracker.events")) {
                    replay.TrackerEvents = MpqTrackerEvents.Parse(DataParser.GetMpqFile(archive, "replay.tracker.events"));
                }


                for (int i = latest_trackever_event; i < replay.TrackerEvents.Count; i++) {
                    //[367] = { StatGameEvent: { "TalentChosen", [{ { "PurchaseName"}, "UtherMasteryWaveofLightHolyRadiance"}], [{ { "PlayerID"}, 2}], } }
                    //"TalentChosen"

                    if (replay.TrackerEvents[i].Data.dictionary[0].blobText == "TalentChosen") {
                        Talent talent = new Talent();

                        talent.TalentName = replay.TrackerEvents[i].Data.dictionary[1].optionalData.array[0].dictionary[1].blobText;
                        long playerID = replay.TrackerEvents[i].Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value;
                        //talent.TalentID = heroTalentIds[replay.Players[playerID - 1].Character + "|" + talent.TalentName];
                        talent.TimeSpanSelected = replay.TrackerEvents[i].TimeSpan;


                        if (!playerIDTalentIndexDictionary.ContainsKey(Convert.ToInt32(playerID - 1)))
                            playerIDTalentIndexDictionary[Convert.ToInt32(playerID - 1)] = 0;

                        //players[playerID - 1].Talents[playerIDTalentIndexDictionary[Convert.ToInt32(playerID - 1)]++] = talent;





                        //int talent_level = heroTalentLevels[replay.Players[playerID - 1].Character + "|" + talent.TalentName];


                        if (start_new_parse) {
                            start_new_parse = false;
                            await saveReplayData(replay);//Save replay data
                            await savePlayerData(replay);
                        }
                        await saveTalentData(replay, replay.Players[playerID - 1], talent);
                    }
                }
                latest_trackever_event = replay.TrackerEvents.Count - 1;
                //Statistics.Parse(replay);
            }


            //if (start_new_parse) {
            //    start_new_parse = false;
            //    await saveReplayData(replay);//Save replay data
            //    await savePlayerData(replay);
            //}


            //await saveReplayData(replay);
            //if (latest_replayID != 0) {
            //    await savePlayerData(replay);
            //}
        }


        private async Task saveReplayData(Replay replay)
        {
            var values = new Dictionary<string, string>
            {
                { "hp_twitch_key", hpTwitchAPIKey },
                { "email", hpAPIEmail },
                { "twitch_nickname", twitchKnickname },
                { "user_id", hpAPIUserID.ToString() },
                { "game_type", replay.GameMode.ToString() },
                { "game_date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                { "game_map", replay.Map },
                { "game_version", replay.ReplayVersion },
                { "region", replay.Players[0].BattleNetRegionId.ToString() },
            };

            var content = new FormUrlEncodedContent(values);

            var response = await client.PostAsync($"{heresprofileAPI}{saveReplayUrl}", content);

            if (response.IsSuccessStatusCode) {

                if (Int32.TryParse(response.Content.ReadAsStringAsync().Result, out int value)) {
                    latest_replayID = value;
                }
            } else {
                _log.Error("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            }
        }


        private async Task savePlayerData(Replay replay)
        {

            for (int i = 0; i < replay.Players.Length; i++) {
                var values = new Dictionary<string, string>
                {
                    { "hp_twitch_key", hpTwitchAPIKey },
                    { "email", hpAPIEmail },
                    { "twitch_nickname", twitchKnickname },
                    { "user_id", hpAPIUserID.ToString() },
                    { "replayID", latest_replayID.ToString() },
                    { "blizz_id", replay.Players[i].BattleNetId.ToString() },
                    { "battletag", replay.Players[i].Name },
                    { "hero", replay.Players[i].Character },
                    { "team", replay.Players[i].Team.ToString() },
                    { "region", replay.Players[i].BattleNetRegionId.ToString() },
                };

                var content = new FormUrlEncodedContent(values);
                var response = await client.PostAsync($"{heresprofileAPI}{savePlayersUrl}", content);
                _log.Info("Saving player data for twitch extension" + response);
            }
        }

        private async Task saveTalentData(Replay replay, Player player, Talent talent)
        {
            var values = new Dictionary<string, string>
            {
                { "hp_twitch_key", hpTwitchAPIKey },
                { "email", hpAPIEmail },
                { "twitch_nickname", twitchKnickname },
                { "user_id", hpAPIUserID.ToString() },
                { "replayID", latest_replayID.ToString() },
                { "blizz_id", player.BattleNetId.ToString() },
                { "battletag", player.Name },
                { "region", player.BattleNetRegionId.ToString() },
                { "talent", talent.TalentName },
                { "hero", player.Character },
            };

            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync($"{heresprofileAPI}{saveTalentsUrl}", content);
            _log.Info("Saving talents for twitch extension" + response);
        }

        public async Task saveTalentDataTwenty(string stormReplayPath)
        {
            var (replayParseResult, replay) = DataParser.ParseReplay(stormReplayPath, deleteFile: false, ParseOptions.DefaultParsing);
            
            foreach (var player in replay.Players.OrderByDescending(i => i.IsWinner)) 
            {
                var values = new Dictionary<string, string>
{
                { "hp_twitch_key", hpTwitchAPIKey },
                { "email", hpAPIEmail },
                { "twitch_nickname", twitchKnickname },
                { "user_id", hpAPIUserID.ToString() },
                { "replayID", latest_replayID.ToString() },
                { "blizz_id", player.BattleNetId.ToString() },
                { "battletag", player.Name },
                { "region", player.BattleNetRegionId.ToString() },
                { "talent", player.Talents[6].TalentName },
                { "hero", player.Character },
            };

                var content = new FormUrlEncodedContent(values);
                var response = await client.PostAsync($"{heresprofileAPI}{saveTalentsUrl}", content);
                _log.Info("Saving level twenties for twitch extension" + response);
            }
        }

    }
}
