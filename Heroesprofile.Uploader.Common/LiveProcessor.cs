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
        public bool TwitchExtension { get; set; }

        public string hpTwitchAPIKey { get; set; }
        public string hpAPIEmail { get; set; }
        public string twitchNickname { get; set; }
        public int hpAPIUserID { get; set; }

        private static Logger _log = LogManager.GetCurrentClassLogger();
        HttpClient client = new HttpClient();


        private static readonly string heresprofile = @"https://www.heroesprofile.com/";
        private static readonly string heresprofileAPI = @"https://api.heroesprofile.com/";


        private static readonly string preMatchURI = @"PreMatch/Results/?prematchID=";

        private static readonly string saveReplayUrl = @"twitch/extension/save/replay";
        private static readonly string updateReplayDataUrl = @"twitch/extension/update/replay/";
        private static readonly string savePlayersUrl = @"twitch/extension/save/player";
        private static readonly string updatePlayerDataUrl = @"twitch/extension/update/player";
        private static readonly string saveTalentsUrl = @"twitch/extension/save/talent";
        private static readonly string notifyUrl = @"twitch/extension/notify/talent/update";

        private bool gameModeUpdated = false;
        private int latest_replayID = 0;
        private int latest_trackever_event = 0;
        private bool talentUpdate = false;
        private Dictionary<int, int> playerIDTalentIndexDictionary = new Dictionary<int, int>();
        private Dictionary<string, string> foundTalents = new Dictionary<string, string>();

        private Replay replayData;

        public LiveProcessor(bool PreMatchPage, bool TwitchExtension, string hpTwitchAPIKey, string hpAPIEmail, string twitchNickname, int hpAPIUserID)
        {
            this.PreMatchPage = PreMatchPage;
            this.TwitchExtension = TwitchExtension;
            this.hpTwitchAPIKey = hpTwitchAPIKey;
            this.hpAPIEmail = hpAPIEmail;
            this.twitchNickname = twitchNickname;
            this.hpAPIUserID = hpAPIUserID;
        }

        public async Task StartProcessing(string battleLobbyPath)
        {
            byte[] replayBytes = File.ReadAllBytes(battleLobbyPath);
            replayData = MpqBattlelobby.Parse(replayBytes);

            if (PreMatchPage) {
                await runPreMatch(replayData);
            }

            if (TwitchExtension) {
                await runTwitchExtensionStart(replayData);
            }

        }

        public async Task UpdateData(string stormSavePath)
        {
            await runTwitchExtensionUpdate(stormSavePath);
        }


        /// <summary>
        /// Upload replay data to Heroes Profile and open up PreMatch page
        /// </summary>
        private async Task runPreMatch(Replay replayData)
        {

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

        private async Task runTwitchExtensionStart(Replay replayData)
        {
            await getNewReplayID();
            await savePlayerData(replayData);
            await notifyTwitchOfTalentChange();
        }



        private async Task runTwitchExtensionUpdate(string stormSavePath)
        {
            if (latest_replayID != 0) {
                var replay = new Replay();
                MpqHeader.ParseHeader(replay, stormSavePath);

                using (var archive = new Foole.Mpq.MpqArchive(stormSavePath)) {
                    archive.AddListfileFilenames();


                    MpqDetails.Parse(replay, DataParser.GetMpqFile(archive, "save.details"), true);

                    if (archive.FileExists("replay.attributes.events")) {
                        MpqAttributeEvents.Parse(replay, DataParser.GetMpqFile(archive, "replay.attributes.events"));
                    }

                    //Get Game Mode               
                    if (archive.FileExists("save.initData") && !gameModeUpdated) {
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

                    if (archive.FileExists("replay.tracker.events")) {
                        replay.TrackerEvents = MpqTrackerEvents.Parse(DataParser.GetMpqFile(archive, "replay.tracker.events"));
                    }

                    //Statistics.Parse(replay);

                    foreach (var battlelobbyPlayer in replayData.Players) {
                        foreach (var stormSavePlayer in replay.Players) {
                            if (battlelobbyPlayer.Name == stormSavePlayer.Name) {
                                stormSavePlayer.BattleTag = battlelobbyPlayer.BattleTag;
                                break;
                            }
                        }
                    }

                    if (replay.TrackerEvents != null) {

                        for (int i = latest_trackever_event; i < replay.TrackerEvents.Count; i++) {
                            if (replay.TrackerEvents[i].Data.dictionary[0].blobText == "TalentChosen") {
                                Talent talent = new Talent();

                                talent.TalentName = replay.TrackerEvents[i].Data.dictionary[1].optionalData.array[0].dictionary[1].blobText;
                                long playerID = replay.TrackerEvents[i].Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value;
                                talent.TimeSpanSelected = replay.TrackerEvents[i].TimeSpan;
                                if (!playerIDTalentIndexDictionary.ContainsKey(Convert.ToInt32(playerID - 1)))
                                    playerIDTalentIndexDictionary[Convert.ToInt32(playerID - 1)] = 0;

                                if (!gameModeUpdated) {
                                    await updateReplayData(replay);
                                    await updatePlayerData(replay);
                                    gameModeUpdated = true;
                                }
                                if (!foundTalents.ContainsKey(replay.Players[playerID - 1].Name+ talent.TalentName)) {
                                    foundTalents.Add(replay.Players[playerID - 1].Name+talent.TalentName, replay.Players[playerID - 1]+talent.TalentName);
                                    await saveTalentData(replay, replay.Players[playerID - 1], talent);
                                    talentUpdate = true;
                                }
                          
                                latest_trackever_event = i;

                            }
                        }
                        
                        if (talentUpdate) {
                            await notifyTwitchOfTalentChange();
                            talentUpdate = false;
                        }
                        
                        
                    }

                    if (!gameModeUpdated) {
                        await updateReplayData(replay);
                        await updatePlayerData(replay);
                        gameModeUpdated = true;
                    }

                }
            }
        }

        

        private async Task getNewReplayID(int loop = 0)
        {
            var values = new Dictionary<string, string>
            {
                { "hp_twitch_key", hpTwitchAPIKey },
                { "email", hpAPIEmail },
                { "twitch_nickname", twitchNickname },
                { "user_id", hpAPIUserID.ToString() },
                { "game_date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
            };

            var content = new FormUrlEncodedContent(values);

            var response = await client.PostAsync($"{heresprofileAPI}{saveReplayUrl}", content);

            if (response.IsSuccessStatusCode) {


                if (Int32.TryParse(response.Content.ReadAsStringAsync().ToString(), out int value)) {
                    latest_replayID = value;
                }
            } else if(response.StatusCode == (HttpStatusCode)429 && loop < 5) {
                await Task.Delay(response.Headers.RetryAfter.Delta.Value);
                loop++;
                await getNewReplayID(loop);
            }
            
            else {
                _log.Error("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            }
        }

        private async Task updateReplayData(Replay replay, int loop = 0)
        {
            var values = new Dictionary<string, string>
{
                { "hp_twitch_key", hpTwitchAPIKey },
                { "email", hpAPIEmail },
                { "twitch_nickname", twitchNickname },
                { "user_id", hpAPIUserID.ToString() },
                { "replayID", latest_replayID.ToString() },
                { "game_type", replay.GameMode.ToString() },
                { "game_map", replay.Map },
                { "game_version", replay.ReplayVersion },
                { "region", replay.Players[0].BattleNetRegionId.ToString() },
            };
            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync($"{heresprofileAPI}{updateReplayDataUrl}", content);
            //_log.Info("Updating Game Mode Data for Live Extension:" + response);

            try {
                if (response.StatusCode == (HttpStatusCode)429 && loop < 5) {


                    await Task.Delay(response.Headers.RetryAfter.Delta.Value);
                    loop++;
                    await updateReplayData(replay, loop);
                }
            }
            catch (Exception e) {
                var error = e.ToString();
            }
        }

        private async Task savePlayerData(Replay replay, int incrementValue = 0, int loop = 0)
        {

            for (int i = incrementValue; i < replay.Players.Length; i++) {
                var values = new Dictionary<string, string>
                {
                    { "hp_twitch_key", hpTwitchAPIKey },
                    { "email", hpAPIEmail },
                    { "twitch_nickname", twitchNickname },
                    { "user_id", hpAPIUserID.ToString() },
                    { "replayID", latest_replayID.ToString() },
                    { "battletag", replay.Players[i].Name + "#" +  replay.Players[i].BattleTag},
                    { "team", replay.Players[i].Team.ToString() },
                };

                var content = new FormUrlEncodedContent(values);
                var response = await client.PostAsync($"{heresprofileAPI}{savePlayersUrl}", content);
                // _log.Info("Saving player data for twitch extension" + response);


                try {
                    if (response.StatusCode == (HttpStatusCode)429 && loop < 5) {
                        await Task.Delay(response.Headers.RetryAfter.Delta.Value);
                        loop++;
                        await savePlayerData(replay, i, loop);
                    }
                }
                catch (Exception e) {
                    var error = e.ToString();
                }
            }
        }

        private async Task updatePlayerData(Replay replay, int incrementValue = 0, int loop = 0)
        {

            for (int i = incrementValue; i < replay.Players.Length; i++) {
                var values = new Dictionary<string, string>
                {
                    { "hp_twitch_key", hpTwitchAPIKey },
                    { "email", hpAPIEmail },
                    { "twitch_nickname", twitchNickname },
                    { "user_id", hpAPIUserID.ToString() },
                    { "replayID", latest_replayID.ToString() },
                    { "blizz_id", replay.Players[i].BattleNetId.ToString() },
                    { "battletag", replay.Players[i].Name + "#" + replay.Players[i].BattleTag},
                    { "hero", replay.Players[i].Character },
                    { "hero_id", replay.Players[i].HeroId },
                    { "hero_attribute_id", replay.Players[i].HeroAttributeId },
                    { "team", replay.Players[i].Team.ToString() },
                    { "region", replay.Players[i].BattleNetRegionId.ToString() },
                };

                var content = new FormUrlEncodedContent(values);
                var response = await client.PostAsync($"{heresprofileAPI}{updatePlayerDataUrl}", content);
                //_log.Info("Updating player data for twitch extension" + response);

                try {
                    if (response.StatusCode == (HttpStatusCode)429 && loop < 5) {


                        await Task.Delay(response.Headers.RetryAfter.Delta.Value);
                        loop++;
                        await updatePlayerData(replay, i, loop);
                    }
                }
                catch (Exception e) {
                    var error = e.ToString();
                }
            }
        }

        private async Task saveTalentData(Replay replay, Player player, Talent talent, int loop = 0)
        {
            var values = new Dictionary<string, string>
            {
                { "hp_twitch_key", hpTwitchAPIKey },
                { "email", hpAPIEmail },
                { "twitch_nickname", twitchNickname },
                { "user_id", hpAPIUserID.ToString() },
                { "replayID", latest_replayID.ToString() },
                { "blizz_id", player.BattleNetId.ToString() },
                { "battletag", player.Name + "#" + player.BattleTag},
                { "region", player.BattleNetRegionId.ToString() },
                { "talent", talent.TalentName },
                { "hero", player.Character },
            };

            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync($"{heresprofileAPI}{saveTalentsUrl}", content);

   

            try {
                if (response.StatusCode == (HttpStatusCode)429 && loop < 5) {

                    
                    await Task.Delay(response.Headers.RetryAfter.Delta.Value);
                    loop++;
                    await saveTalentData(replay, player, talent, loop);
                }
            }
            catch (Exception e) {
                var error = e.ToString();
            }

         
           // _log.Info("Saving talents for twitch extension" + response);
        }

        public async Task saveMissingTalentData(string stormReplayPath)
        {
            if(latest_replayID != 0) {
                var (replayParseResult, replay) = DataParser.ParseReplay(stormReplayPath, deleteFile: false, ParseOptions.DefaultParsing);
                if (replay != null) {
                    foreach (var player in replay.Players.OrderByDescending(i => i.IsWinner)) {

                   
                        if (player.Talents != null) {
                            for (int i = 0; i < player.Talents.Length; i++) {
                                if (!foundTalents.ContainsKey(player.Name + player.Talents[i].TalentName)) {
                                    await saveTalentData(replay, player, player.Talents[i]);
                                }
                            }
                        }
                    }
                    await notifyTwitchOfTalentChange();
                }
            }
        }

        private async Task notifyTwitchOfTalentChange(int loop = 0)
        {
            var values = new Dictionary<string, string>
{
                { "hp_twitch_key", hpTwitchAPIKey },
                { "email", hpAPIEmail },
                { "twitch_nickname", twitchNickname },
                { "user_id", hpAPIUserID.ToString() },
            };
            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync($"{heresprofileAPI}{notifyUrl}", content);

            try {

                if (response.StatusCode == (HttpStatusCode)429 && loop < 5) {

                    await Task.Delay(response.Headers.RetryAfter.Delta.Value);
                    loop++;
                    await notifyTwitchOfTalentChange(loop);
                }
            }
            catch (Exception e) {
                var error = e.ToString();
            }

            //_log.Info("Updating Game Mode Data for Live Extension:" + response);
        }
    }
}
