using Heroesprofile.Uploader.Common;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Sends a game's ranks to Heroes Profile, tied to its replay by the replay's id on the site. Only cards
    /// identified by their player's name or hero go: a card placed by elimination might be anyone's. Each
    /// player is named by full BattleTag and region, from the finished replay - how the site knows players.
    /// The request is signed (<see cref="UploaderSigning"/>): the site only takes ranks from released builds.
    /// </summary>
    internal static class RankUploader
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

#if DEBUG
        private const string Endpoint = "http://127.0.0.1:8000/api/external/v1/ranks";
#else
        private const string Endpoint = "https://www.heroesprofile.com/api/external/v1/ranks";
#endif

        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };

        /// <summary>A player as the finished replay has them.</summary>
        public sealed record ReplayPlayer(string Name, int BattleTag, int Region, int Team, string Hero);

        /// <summary>What's sent for one game: the replay's id, and each identified player's rank.</summary>
        public static object Payload(int replayId, IReadOnlyList<ReplayPlayer> players, IReadOnlyList<PlayerRanks.PlayerRank> ranks) => new {
            replayID = replayId,
            uploaderVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            ranks = ranks.Where(r => r.Match.Identified && players[r.Player].BattleTag > 0).Select(r => new {
                battletag = $"{players[r.Player].Name}#{players[r.Player].BattleTag}",
                region = players[r.Player].Region,
                league = r.Rank.League.ToString(),
                division = r.Rank.Division,
                ladderPosition = r.Rank.LadderPosition,
                nameConfidence = Math.Round(r.Match.NameConfidence, 2),
                heroConfidence = Math.Round(r.Match.HeroConfidence, 2),
            }).ToList(),
        };

        public static async Task SendAsync(int replayId, IReadOnlyList<ReplayPlayer> players, IReadOnlyList<PlayerRanks.PlayerRank> ranks)
        {
            try {
                if (!UploaderSigning.IsAvailable) {
                    // Heroes Profile only takes ranks from released builds, which carry a signing key.
                    _log.Info($"Rank reading: this build can't sign, so the ranks for replay {replayId} aren't sent");
                    return;
                }
                var json = JsonConvert.SerializeObject(Payload(replayId, players, ranks));
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                UploaderSigning.Sign(request.Headers, json);
                using var reply = await _client.SendAsync(request);
                if (reply.IsSuccessStatusCode) {
                    _log.Info($"Rank reading: sent the ranks for replay {replayId}");
                } else {
                    var body = await reply.Content.ReadAsStringAsync();
                    _log.Warn($"Rank reading: ranks for replay {replayId} not taken: HTTP {(int)reply.StatusCode} {(body.Length > 300 ? body.Substring(0, 300) + "..." : body)}");
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, $"Rank reading: couldn't send the ranks for replay {replayId}");
            }
        }
    }
}
