using Heroes.ReplayParser;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Beta only: sends a Storm League game's loading-screen card strips to Heroes Profile, where they're kept
    /// in a private storage folder to build and test the rank reader - a few frames per game, with what the
    /// reader made of them. The server's answer says whether to keep collecting; once it says no, this stops
    /// for the rest of the run.
    /// </summary>
    internal static class SampleUploader
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

#if DEBUG
        private const string Endpoint = "http://127.0.0.1:8000/api/external/v1/rank-samples";
#else
        private const string Endpoint = "https://www.heroesprofile.com/api/external/v1/rank-samples";
#endif

        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        /// <summary>False once the server has said it has enough samples.</summary>
        public static bool Collecting { get; private set; } = true;

        /// <summary>The most frames sent per game: the loading screen doesn't change, so a few are plenty.</summary>
        public const int MaxFramesSent = 3;

        public static async Task UploadAsync(GameCapture game, IReadOnlyList<CapturedFrame> frames, Replay stormSave, RankReader.GameReading reading)
        {
            if (!Collecting || frames.Count == 0) {
                return;
            }
            var send = FramesToSend(frames.Count, reading?.LoadingScreenFrames);

            var meta = new {
                uploaderVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                os = RuntimeInformation.OSDescription,
                gameMode = stormSave.GameMode.ToString(),
                map = stormSave.Map,
                gameVersion = stormSave.ReplayVersion,
                lobbyAt = game.LobbyAt,
                // When the first storm save came (~80 s into the match), not when the match started.
                firstStormSaveAt = DateTime.UtcNow,
                players = (game.Lobby.Players ?? Array.Empty<Player>()).Where(p => p != null).Select(p => new {
                    name = p.Name,
                    battletag = p.BattleTag,
                    region = p.BattleNetRegionId,
                    team = p.Team,
                    // From the storm save, which knows the heroes (the lobby file doesn't).
                    hero = (stormSave.Players ?? Array.Empty<Player>()).FirstOrDefault(s => s != null && s.BattleTag == p.BattleTag && s.Name == p.Name)?.Character,
                }),
                frames = frames.Select((f, i) => new {
                    index = i,
                    capturedAt = f.At,
                    width = f.Width,
                    height = f.Height,
                    loadingScreen = reading?.LoadingScreenFrames.Contains(i),
                    sent = send.Contains(i),
                }),
                // What the reader made of the cards, to check it against the frames. Who each card is gets
                // settled once the finished replay is in, so isn't here.
                cards = reading?.Cards?.Select(c => new {
                    card = (c.Rank.Side == Side.Left ? "L" : "R") + (c.Rank.Slot + 1),
                    rank = c.Rank.Rank,
                    league = c.Rank.League.ToString(),
                    division = c.Rank.Division,
                    ladderPosition = c.Rank.LadderPosition,
                    names = c.Text.Names,
                    heroes = c.Text.Heroes,
                }),
                textReaders = CardText.Languages,
            };

            // One request per frame (its two strips, a few MB) keeps each well under the site's PHP
            // post_max_size (12 MB) - all of a game's frames together can be more. The shared sample id puts
            // them in one folder on the server.
            var sampleId = Guid.NewGuid().ToString();
            var metaJson = JsonConvert.SerializeObject(meta);
            var sent = 0;
            foreach (var i in send) {
                if (!Collecting) {
                    break;
                }
                if (await SendFrameAsync(sampleId, metaJson, i, frames[i])) {
                    sent++;
                }
            }
            if (sent > 0) {
                _log.Info($"Rank reading: sent {sent} of {frames.Count} captured frames as samples for this Storm League game");
            }
        }

        /// <summary>
        /// Which frames to send (indexes, in order): up to <see cref="MaxFramesSent"/> of those that show the
        /// loading screen, spread from first to last. When none do (or reading failed), one frame from the
        /// middle, so a capture that missed the loading screen still shows what it caught.
        /// </summary>
        public static IReadOnlyList<int> FramesToSend(int count, IReadOnlyList<int> loadingScreen)
        {
            if (count == 0) {
                return Array.Empty<int>();
            }
            if (loadingScreen == null || loadingScreen.Count == 0) {
                return new[] { count / 2 };
            }
            if (loadingScreen.Count <= MaxFramesSent) {
                return loadingScreen.ToList();
            }
            return Enumerable.Range(0, MaxFramesSent)
                .Select(k => loadingScreen[(int)Math.Round(k * (loadingScreen.Count - 1) / (double)(MaxFramesSent - 1))])
                .Distinct()
                .ToList();
        }

        /// <summary>Sends one frame; false if it didn't go (and why is logged).</summary>
        private static async Task<bool> SendFrameAsync(string sampleId, string metaJson, int index, CapturedFrame frame)
        {
            try {
                using var form = new MultipartFormDataContent {
                    { new StringContent(sampleId), "sample" },
                    { new StringContent(metaJson), "meta" },
                    { Png(frame.LeftPng), "frames[]", $"frame-{index}-left.png" },
                    { Png(frame.RightPng), "frames[]", $"frame-{index}-right.png" },
                };

                using var reply = await _client.PostAsync(Endpoint, form);
                var body = await reply.Content.ReadAsStringAsync();

                JObject json;
                try {
                    json = JObject.Parse(body);
                }
                catch (JsonException) {
                    // Not our endpoint's answer: a proxy or PHP itself (e.g. its post size limit) answered.
                    _log.Warn($"Rank reading: sample upload got a non-JSON answer: HTTP {(int)reply.StatusCode} {Describe(body)}");
                    return false;
                }

                if (json["collect"] is JToken collect && collect.Type == JTokenType.Boolean && !(bool)collect) {
                    Collecting = false;
                    _log.Info("Rank reading: Heroes Profile isn't collecting loading-screen samples right now - not sending any more");
                }
                if (!reply.IsSuccessStatusCode || json["stored"]?.Type != JTokenType.Boolean || !(bool)json["stored"]) {
                    if (Collecting) {
                        _log.Warn($"Rank reading: sample upload refused: HTTP {(int)reply.StatusCode} {Describe(body)}");
                    }
                    return false;
                }
                return true;
            }
            catch (Exception ex) {
                _log.Warn(ex, "Rank reading: sample upload failed");
                return false;
            }
        }

        /// <summary>A response body for the log: enough to see what answered, not a whole page.</summary>
        private static string Describe(string body)
        {
            var oneLine = (body ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length > 300 ? oneLine.Substring(0, 300) + "..." : oneLine;
        }

        private static ByteArrayContent Png(byte[] bytes)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return content;
        }
    }
}
