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
    /// in a private storage folder to build and test the rank reader. The server's answer says whether to
    /// keep collecting; once it says no, this stops for the rest of the run.
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

        public static async Task UploadAsync(GameCapture game, IReadOnlyList<CapturedFrame> frames, Replay stormSave)
        {
            if (!Collecting || frames.Count == 0) {
                return;
            }

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
                }),
                frames = frames.Select((f, i) => new { index = i, capturedAt = f.At, width = f.Width, height = f.Height }),
            };

            // One request per frame (its two strips, a few MB) keeps each well under the site's PHP
            // post_max_size (12 MB) - all of a game's frames together can be more. The shared sample id puts
            // them in one folder on the server.
            var sampleId = Guid.NewGuid().ToString();
            var metaJson = JsonConvert.SerializeObject(meta);
            var sent = 0;
            for (var i = 0; i < frames.Count && Collecting; i++) {
                if (await SendFrameAsync(sampleId, metaJson, i, frames[i])) {
                    sent++;
                }
            }
            if (sent > 0) {
                _log.Info($"Rank reading: sent {sent} of {frames.Count} loading-screen samples for this Storm League game");
            }
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
