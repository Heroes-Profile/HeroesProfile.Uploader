using Heroes.ReplayParser;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MpqAttributeEvents = Heroes.ReplayParser.MPQFiles.ReplayAttributeEvents;
using MpqDetails = Heroes.ReplayParser.MPQFiles.ReplayDetails;
using MpqHeader = Heroes.ReplayParser.MPQFiles.MpqHeader;
using MpqInitData = Heroes.ReplayParser.MPQFiles.ReplayInitData;
using MpqTrackerEvents = Heroes.ReplayParser.MPQFiles.ReplayTrackerEvents;

namespace Heroesprofile.Uploader.Common
{
    /// <summary>
    /// Feeds the Heroes Profile Twitch extension while a game is being played.
    ///
    /// One game is one session: it starts when the battle lobby appears, grows as the
    /// game writes .StormSave files (heroes, then each talent tier), and ends when the
    /// replay lands. Every post is the whole game so far, so a lost or reordered post
    /// fixes itself on the next one. Posts go out only when something changed, and no
    /// more than once every few seconds; picks that land together share one post.
    /// </summary>
    public class TwitchLiveSession
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

#if DEBUG
        private static readonly string twitchAPI = @"http://127.0.0.1:8000/api/twitch/v1/";
#else
        private static readonly string twitchAPI = @"https://www.heroesprofile.com/api/twitch/v1/";
#endif

        /// <summary>Minimum gap between posts. All ten level-one picks go out together.</summary>
        private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(8);

        private readonly object _lock = new object();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private string _gameId;
        private int _seq;
        private string _phase;
        private string _gameMode;
        private string _map;
        private string _gameVersion;
        private List<LivePlayer> _players = new List<LivePlayer>();
        private int _trackerEventsSeen;
        private string _lastSentHash;
        private DateTime _lastSentAt = DateTime.MinValue;
        private bool _sendPending;
        private bool _stoppedForGame;

        /// <summary>Uploader key from the Heroes Profile API portal.</summary>
        public string Key { get; set; }

        public bool Enabled { get; set; }

        /// <summary>Raised with a line for the UI: what was last sent, or why sending stopped.</summary>
        public event EventHandler<EventArgs<string>> StatusChanged;

        public bool HasGame { get { lock (_lock) { return _gameId != null; } } }

        /// <summary>A new battle lobby: a new game, even if the last one never ended.</summary>
        public Task StartGame(Replay battleLobby)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(Key)) {
                return Task.CompletedTask;
            }

            lock (_lock) {
                _gameId = Guid.NewGuid().ToString();
                _seq = 0;
                _phase = "lobby";
                _gameMode = null;
                _map = null;
                _gameVersion = null;
                _trackerEventsSeen = 0;
                _lastSentHash = null;
                _stoppedForGame = false;
                _players = battleLobby.Players
                    // Players only: observers sit outside teams 0 and 1.
                    .Where(p => p != null && !string.IsNullOrEmpty(p.Name) && (p.Team == 0 || p.Team == 1))
                    .Take(10)
                    .Select(p => new LivePlayer {
                        Name = p.Name,
                        BattleTag = p.BattleTag,
                        Region = p.BattleNetRegionId,
                        Team = p.Team,
                    })
                    .ToList();
            }

            return QueueSend();
        }

        /// <summary>
        /// A .StormSave from the game in progress: heroes, mode, map, and every talent
        /// picked so far. Tracker events only grow, so each file is read from where the
        /// last one stopped.
        /// </summary>
        public Task UpdateFromStormSave(string stormSavePath)
        {
            if (!Enabled || !HasGame) {
                return Task.CompletedTask;
            }

            Replay save;
            try {
                save = ParseLiveFile(stormSavePath, isFinalReplay: false);
            }
            catch (Exception ex) {
                _log.Warn(ex, $"Could not read storm save '{stormSavePath}' for the Twitch extension");
                return Task.CompletedTask;
            }

            lock (_lock) {
                _phase = "in_game";
                _gameMode = save.GameMode.ToString();
                _map = save.Map;
                _gameVersion = save.ReplayVersion;

                if (save.Players != null) {
                    var lobbyRegion = _players.Select(p => p.Region).FirstOrDefault(r => r > 0);

                    foreach (var savePlayer in save.Players) {
                        if (savePlayer == null || (savePlayer.Team != 0 && savePlayer.Team != 1)) {
                            continue;
                        }

                        // Matched on name: the save has no battletag. Two players sharing a
                        // name is rarer than an observer shifting every index along.
                        var live = _players.FirstOrDefault(p => p.Name == savePlayer.Name);

                        // AI players are not in the lobby file, which only lists battletags.
                        if (live == null && savePlayer.PlayerType == PlayerType.Computer && _players.Count < 10) {
                            live = new LivePlayer {
                                Name = string.IsNullOrWhiteSpace(savePlayer.Name) ? savePlayer.Character : savePlayer.Name,
                                BattleTag = 0,
                                Region = lobbyRegion,
                                IsAI = true,
                            };
                            _players.Add(live);
                        }

                        if (live == null) {
                            continue;
                        }

                        // The name the details file carries, which is the hero actually being
                        // played. The attribute events hold something else in ARAM - whatever the
                        // player was last on - so it only goes along as a fallback for a client
                        // whose language the site cannot match the name in.
                        live.Hero = !string.IsNullOrEmpty(savePlayer.Character) ? savePlayer.Character : savePlayer.HeroAttributeId;
                        live.HeroAttribute = savePlayer.HeroAttributeId;
                        // The lobby only guesses teams from slot order; the save knows.
                        live.Team = savePlayer.Team;
                        if (live.Region <= 0 && savePlayer.BattleNetRegionId > 0) {
                            live.Region = savePlayer.BattleNetRegionId;
                        }
                    }

                    // An AI added before any region was known takes the first real one.
                    var region = _players.Select(p => p.Region).FirstOrDefault(r => r > 0);
                    foreach (var p in _players.Where(p => p.Region <= 0)) {
                        p.Region = region;
                    }
                }

                if (save.TrackerEvents != null && save.Players != null) {
                    for (var i = _trackerEventsSeen; i < save.TrackerEvents.Count; i++) {
                        ApplyTalentEvent(save, save.TrackerEvents[i]);
                    }
                    _trackerEventsSeen = save.TrackerEvents.Count;
                }
            }

            return QueueSend();
        }

        /// <summary>
        /// The finished replay. Fills in any talent the last storm save missed — a
        /// level 20 pick moments before the core fell — and closes the game.
        /// </summary>
        public Task EndGame(string replayPath)
        {
            if (!Enabled || !HasGame) {
                return Task.CompletedTask;
            }

            Replay replay;
            try {
                replay = ParseLiveFile(replayPath, isFinalReplay: true);
            }
            catch (Exception ex) {
                _log.Warn(ex, $"Could not read replay '{replayPath}' for the Twitch extension");
                return Task.CompletedTask;
            }

            lock (_lock) {
                // Only if it is the same game: a replay from a backlog scan is not.
                var names = new HashSet<string>(_players.Select(p => p.Name));
                if (replay.Players == null || !replay.Players.All(p => names.Contains(p.Name))) {
                    return Task.CompletedTask;
                }

                foreach (var player in replay.Players) {
                    var live = _players.First(p => p.Name == player.Name);
                    if (string.IsNullOrEmpty(live.Hero)) {
                        live.Hero = !string.IsNullOrEmpty(player.Character) ? player.Character : player.HeroAttributeId;
                        live.HeroAttribute = player.HeroAttributeId;
                    }
                }

                // The whole game's events, not just the tail: already-known picks are
                // skipped, so this only ever adds what was missed.
                if (replay.TrackerEvents != null) {
                    foreach (var trackerEvent in replay.TrackerEvents) {
                        ApplyTalentEvent(replay, trackerEvent);
                    }
                }

                _phase = "ended";
            }

            return SendNow();
        }

        /// <summary>Checks a key from the settings screen.</summary>
        public static async Task<string> Validate(string key)
        {
            try {
                using (var request = new HttpRequestMessage(HttpMethod.Get, $"{twitchAPI}uploader/whoami")) {
                    request.Headers.Add("X-HP-Twitch-Key", key?.Trim() ?? "");
                    request.Headers.Add("Accept", "application/json");

                    var response = await client.SendAsync(request);
                    var body = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode == HttpStatusCode.Unauthorized) {
                        return "That key is not valid. Create a new one at heroesprofile.com/Api/Account.";
                    }
                    if (!response.IsSuccessStatusCode) {
                        return $"Heroes Profile answered {(int)response.StatusCode}. Try again in a minute.";
                    }

                    var json = JObject.Parse(body);
                    var channel = (string)json["twitch_display_name"] ?? (string)json["twitch_login"];
                    var active = (bool?)json["entitlement"]?["active"] ?? false;
                    var playerLinked = (bool?)json["player_linked"] ?? false;

                    var message = $"Connected to {channel}.";
                    message += active ? " The extension is active." : " The extension is not active — see your Heroes Profile account.";
                    if (!playerLinked) {
                        message += " Connect your Battle.net account there too, so your team is shown first.";
                    }
                    return message;
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, "Twitch key validation failed");
                return "Could not reach Heroes Profile. Check your connection and try again.";
            }
        }

        private void ApplyTalentEvent(Replay save, Heroes.ReplayParser.MPQFiles.TrackerEvent trackerEvent)
        {
            try {
                if (trackerEvent.Data?.dictionary == null || trackerEvent.Data.dictionary[0].blobText != "TalentChosen") {
                    return;
                }

                var talentName = trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[1].blobText;
                var playerIndex = (int)trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value - 1;

                if (playerIndex < 0 || playerIndex >= save.Players.Length) {
                    return;
                }

                var live = _players.FirstOrDefault(p => p.Name == save.Players[playerIndex].Name);
                if (live != null && !live.Talents.Contains(talentName) && live.Talents.Count < 7) {
                    live.Talents.Add(talentName);
                }
            }
            catch (Exception ex) {
                _log.Debug($"Skipped a tracker event the Twitch extension could not read: {ex.Message}");
            }
        }

        /// <summary>
        /// Just the parts the extension needs - details (heroes, map), attributes (game mode), init data and
        /// tracker events. A storm save names its files `save.*` where a finished replay has `replay.*`; the
        /// events are named the same. Also what Manager reads a storm save's game mode with, for the lobby
        /// reader (see Manager.SetStormSaveReader).
        /// </summary>
        public static Replay ParseLiveFile(string path, bool isFinalReplay)
        {
            var replay = new Replay();
            MpqHeader.ParseHeader(replay, path);

            var detailsFile = isFinalReplay ? MpqDetails.FileName : "save.details";
            var initDataFile = isFinalReplay ? MpqInitData.FileName : "save.initData";

            using (var archive = new Foole.Mpq.MpqArchive(path)) {
                archive.AddListfileFilenames();

                MpqDetails.Parse(replay, DataParser.GetMpqFile(archive, detailsFile), true);

                if (archive.FileExists(MpqAttributeEvents.FileName)) {
                    MpqAttributeEvents.Parse(replay, DataParser.GetMpqFile(archive, MpqAttributeEvents.FileName));
                }
                if (archive.FileExists(initDataFile)) {
                    MpqInitData.Parse(replay, DataParser.GetMpqFile(archive, initDataFile));
                }
                if (archive.FileExists(MpqTrackerEvents.FileName)) {
                    replay.TrackerEvents = MpqTrackerEvents.Parse(DataParser.GetMpqFile(archive, MpqTrackerEvents.FileName));
                }
            }

            return replay;
        }

        /// <summary>
        /// Sends now if the last post was long enough ago, otherwise once the gap has
        /// passed. Only one send is ever waiting; it carries whatever is newest then.
        /// </summary>
        private async Task QueueSend()
        {
            TimeSpan wait;
            lock (_lock) {
                if (_sendPending) {
                    return;
                }
                wait = MinInterval - (DateTime.UtcNow - _lastSentAt);
                _sendPending = true;
            }

            if (wait > TimeSpan.Zero) {
                await Task.Delay(wait);
            }

            lock (_lock) {
                _sendPending = false;
            }

            await SendNow();
        }

        private async Task SendNow()
        {
            await _sendLock.WaitAsync();
            try {
                string body;
                lock (_lock) {
                    if (_gameId == null || _stoppedForGame) {
                        return;
                    }

                    var state = new {
                        game_mode = _gameMode,
                        map = _map,
                        game_version = _gameVersion,
                        phase = _phase,
                        players = _players.Select(p => new {
                            name = p.Name,
                            battletag = p.BattleTag,
                            region = p.Region,
                            team = p.Team,
                            hero = p.Hero,
                            hero_attribute = p.HeroAttribute,
                            talents = p.Talents,
                            ai = p.IsAI,
                        }),
                    };

                    var hash = JsonConvert.SerializeObject(state);
                    if (hash == _lastSentHash) {
                        return;
                    }
                    _lastSentHash = hash;
                    _seq++;

                    var payload = JObject.FromObject(state);
                    payload["game_id"] = _gameId;
                    payload["seq"] = _seq;
                    body = payload.ToString(Formatting.None);
                }

                await Post(body);

                lock (_lock) {
                    _lastSentAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, "Twitch extension update failed");
            }
            finally {
                _sendLock.Release();
            }
        }

        private async Task Post(string body, int attempt = 0)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, $"{twitchAPI}uploader/snapshot")) {
                request.Headers.Add("X-HP-Twitch-Key", Key.Trim());
                request.Headers.Add("Accept", "application/json");
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode) {
                    RaiseStatus($"Twitch extension updated ({DateTime.Now:t}).");
                    return;
                }

                if (response.StatusCode == (HttpStatusCode)429 && attempt < 3) {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                    await Task.Delay(retryAfter);
                    await Post(body, attempt + 1);
                    return;
                }

                var text = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == HttpStatusCode.Unauthorized || (int)response.StatusCode == 402) {
                    // No point retrying either for the rest of this game.
                    lock (_lock) {
                        _stoppedForGame = true;
                    }
                    string message = null;
                    try {
                        message = (string)JObject.Parse(text)["message"];
                    }
                    catch {
                    }
                    RaiseStatus(message ?? "Twitch extension stopped: check your key at heroesprofile.com/Api/Account.");
                    return;
                }

                _log.Warn($"Twitch extension update refused: HTTP {(int)response.StatusCode} {text?.Substring(0, Math.Min(300, text?.Length ?? 0))}");
            }
        }

        private void RaiseStatus(string message)
        {
            _log.Info(message);
            StatusChanged?.Invoke(this, new EventArgs<string>(message));
        }

        private class LivePlayer
        {
            public string Name { get; set; }
            public int BattleTag { get; set; }
            public int Region { get; set; }
            public int Team { get; set; }
            public string Hero { get; set; }
            public string HeroAttribute { get; set; }
            public List<string> Talents { get; set; } = new List<string>();
            public bool IsAI { get; set; }
        }
    }
}
