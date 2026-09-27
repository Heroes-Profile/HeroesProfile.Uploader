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
                matchStartedAt = DateTime.UtcNow,
                players = (game.Lobby.Players ?? Array.Empty<Player>()).Where(p => p != null).Select(p => new {
                    name = p.Name,
                    battletag = p.BattleTag,
                    region = p.BattleNetRegionId,
                    team = p.Team,
                }),
                frames = frames.Select((f, i) => new { index = i, capturedAt = f.At, width = f.Width, height = f.Height }),
            };

            try {
                using var form = new MultipartFormDataContent {
                    { new StringContent(JsonConvert.SerializeObject(meta)), "meta" },
                };
                for (var i = 0; i < frames.Count; i++) {
                    form.Add(Png(frames[i].LeftPng), "frames[]", $"frame-{i}-left.png");
                    form.Add(Png(frames[i].RightPng), "frames[]", $"frame-{i}-right.png");
                }

                using var reply = await _client.PostAsync(Endpoint, form);
                var body = await reply.Content.ReadAsStringAsync();
                if (!reply.IsSuccessStatusCode) {
                    _log.Warn($"Rank reading: sample upload failed: HTTP {(int)reply.StatusCode}");
                    return;
                }

                var collect = JObject.Parse(body)["collect"];
                if (collect != null && collect.Type == JTokenType.Boolean && !(bool)collect) {
                    Collecting = false;
                    _log.Info("Rank reading: Heroes Profile has enough loading-screen samples - not sending any more");
                } else {
                    _log.Info($"Rank reading: sent {frames.Count} loading-screen samples for this Storm League game");
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, "Rank reading: sample upload failed");
            }
        }

        private static ByteArrayContent Png(byte[] bytes)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return content;
        }
    }
}
