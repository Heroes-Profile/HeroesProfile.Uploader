using Heroes.ReplayParser;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Heroesprofile.Uploader.Common
{
    public class Analyzer : IAnalyzer
    {
        public int MinimumBuild { get; set; }

        private static Logger _log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Analyze replay locally before uploading
        /// </summary>
        /// <param name="file">Replay file</param>
        public Replay Analyze(ReplayFile file)
        {
            try {
                //filename, ignoreerrors, deletefile, allowptrregion, skipeventparsing
                var (parseResult, replay) = DataParser.ParseReplay(file.Filename, false, ParseOptions.MediumParsing);

                var status = GetPreStatus(replay, parseResult);

                if (status != null) {
                    file.UploadStatus = status.Value;
                }

                if (parseResult != DataParser.ReplayParseResult.Success) {
                    return null;
                }

                file.Fingerprint = GetFingerprint(replay);
                return replay;
            }
            catch (Exception e) {
                _log.Warn(e, $"Error analyzing file {file}");
                return null;
            }
        }

        public UploadStatus? GetPreStatus(Replay replay, DataParser.ReplayParseResult parseResult)
        {
            switch (parseResult) {
                case DataParser.ReplayParseResult.ComputerPlayerFound:
                case DataParser.ReplayParseResult.TryMeMode:
                    return UploadStatus.AiDetected;

                case DataParser.ReplayParseResult.PTRRegion:
                    return UploadStatus.PtrRegion;

                case DataParser.ReplayParseResult.PreAlphaWipe:
                    return UploadStatus.TooOld;
            }

            if (parseResult != DataParser.ReplayParseResult.Success) {
                return null;
            }

            if (replay.GameMode == GameMode.Custom) {
                return UploadStatus.CustomGame;
            }

            if (replay.ReplayBuild < MinimumBuild) {
                return UploadStatus.TooOld;
            }

            return null;
        }

        /// <summary>
        /// Get unique hash of replay. Compatible with HotsLogs
        /// </summary>
        /// <param name="replay"></param>
        /// <returns></returns>
        public string GetFingerprint(Replay replay)
        {
            var str = new StringBuilder();
            replay.Players.Select(p => p.BattleNetId).OrderBy(x => x).Map(x => str.Append(x.ToString()));
            str.Append(replay.RandomValue);
            var md5 = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(str.ToString()));
            var result = new Guid(md5);
            return result.ToString();
        }

        /// <summary>
        /// Swaps two bytes in a byte array
        /// </summary>
        private void SwapBytes(byte[] buf, int i, int j)
        {
            byte temp = buf[i];
            buf[i] = buf[j];
            buf[j] = temp;
        }

        public object ToJson(Replay replay)
        {
            var obj = new {
                mode = replay.GameMode.ToString(),
                region = replay.Players[0].BattleNetRegionId,
                date = replay.Timestamp,
                length = replay.ReplayLength,
                map = replay.Map,
                map_short = replay.MapAlternativeName,
                version = replay.ReplayVersion,
                version_major = replay.ReplayVersionMajor,
                version_build = replay.ReplayBuild,
                bans = replay.TeamHeroBans,
                draft_order = replay.DraftOrder,
                team_experience = replay.TeamPeriodicXPBreakdown,
                players = from p in replay.Players
                          select new {
                              battletag_name = p.Name,
                              battletag_id = p.BattleTag,
                              blizz_id = p.BattleNetId,
                              account_level = p.AccountLevel,
                              hero = p.Character,
                              hero_level = p.CharacterLevel,
                              hero_level_taunt = p.HeroMasteryTiers,
                              team = p.Team,
                              winner = p.IsWinner,
                              silenced = p.IsSilenced,
                              party = p.PartyValue,
                              talents = p.Talents.Select(t => t.TalentName),
                              score = p.ScoreResult,
                              staff = p.IsBlizzardStaff,
                              announcer = p.AnnouncerPackAttributeId,
                              banner = p.BannerAttributeId,
                              skin_title = p.SkinAndSkinTint,
                              hero_skin = p.SkinAndSkinTintAttributeId,
                              mount_title = p.MountAndMountTint,
                              mount = p.MountAndMountTintAttributeId,
                              spray_title = p.Spray,
                              spray = p.SprayAttributeId,
                              voice_line_title = p.VoiceLine,
                              voice_line = p.VoiceLineAttributeId,
                          }
            };
            return JsonConvert.SerializeObject(obj);
        }
    }
}
