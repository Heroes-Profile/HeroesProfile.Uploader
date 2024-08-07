﻿using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using System.IO;
using System.Diagnostics;
using System.Reflection;

namespace Heroesprofile.Uploader.Common
{
    public class Uploader : IUploader
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
#if DEBUG
        const string HeroesProfileApiEndpoint = "http://127.0.0.1:8000/api";
        const string HeroesProfileMatchParsed = "http://127.0.0.1:8000/openApi/Replay/Parsed/?replayID=";
        const string HeroesProfileMatchSummary = "http://localhost/Match/Single/?replayID=";



#else
        const string HeroesProfileApiEndpoint = "https://api.heroesprofile.com/api";
        const string HeroesProfileMatchParsed = "https://api.heroesprofile.com/openApi/Replay/Parsed/?replayID=";
        const string HeroesProfileMatchSummary = "https://www.heroesprofile.com/Match/Single/?replayID=";
#endif

        /// <summary>
        /// New instance of replay uploader
        /// </summary>
        public Uploader()
        {

        }

        /// <summary>
        /// Upload replay
        /// </summary>
        /// <param name="file"></param>
        public async Task Upload(Replay replay_results, ReplayFile file, bool PostMatchPage)
        {
            file.UploadStatus = UploadStatus.InProgress;
            if (file.Fingerprint != null && await CheckDuplicate(file.Fingerprint)) {
                _log.Debug($"File {file} marked as duplicate");
                file.UploadStatus = UploadStatus.Duplicate;
            } else {
                file.UploadStatus = await Upload(replay_results, file.Fingerprint, file.Filename, PostMatchPage);
            }
        }

        /// <summary>
        /// Upload replay
        /// </summary>
        /// <param name="file">Path to file</param>
        /// <returns>Upload result</returns>
        public async Task<UploadStatus> Upload(Replay replay_results, string fingerprint, string file, bool PostMatchPage)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            AssemblyName assemblyName = assembly.GetName();
            Version assemblyVersion = assemblyName.Version;

            try {
                string response;
                using (var client = new WebClient()) {
                    var bytes = await client.UploadFileTaskAsync($"{HeroesProfileApiEndpoint}/upload/heroesprofile/desktop?fingerprint={fingerprint}&version={assemblyVersion}", file);
                    response = Encoding.UTF8.GetString(bytes);
                }

                UploadResult result = UploadResult.FromJson(response);

                try {
                    int replayID = result.ReplayId;
                    if (File.GetLastWriteTime(file) >= DateTime.Now.Subtract(TimeSpan.FromMinutes(60)) && PostMatchPage && replayID != 0) {
                        await postMatchAnalysis(replayID);
                    }
                }
                catch {
                    _log.Error($"Postmatch failed");
                }


                if (!string.IsNullOrEmpty(result.Status)) {
                    if (Enum.TryParse<UploadStatus>((string)result.Status, out UploadStatus status)) {
                        _log.Debug($"Uploaded file '{file}': {status}");
                        return status;
                    } else {
                        _log.Error($"Unknown upload status '{file}': {result.Status}");
                        return UploadStatus.UploadError;
                    }
                } else {
                    _log.Warn($"Error uploading file '{file}': {response}");
                    return UploadStatus.UploadError;
                }

          
            }
            catch (WebException ex) {
                if (await CheckApiThrottling(ex.Response)) {
                    return await Upload(replay_results, fingerprint, file, PostMatchPage);
                }
                _log.Warn(ex, $"Error uploading file '{file}'");
                return UploadStatus.UploadError;
            }
        }

        private async Task postMatchAnalysis(int replayID)
        {
            
            var timer = new Stopwatch();
            timer.Start();
            while (timer.ElapsedMilliseconds < 15000) {
                string response;
                using (var client = new WebClient()) {
                    response = await client.DownloadStringTaskAsync($"{HeroesProfileMatchParsed}{replayID}");
                }
                if (response == "true") {
                    Process.Start($"{HeroesProfileMatchSummary}{replayID}");
                    return;
                }
                await Task.Delay(1000);
            }
            timer.Stop();
        }
        /// <summary>
        /// Check replay fingerprint against database to detect duplicate
        /// </summary>
        /// <param name="fingerprint"></param>
        private async Task<bool> CheckDuplicate(string fingerprint)
        {
            try {
                string response;
                using (var client = new WebClient()) {
                    response = await client.DownloadStringTaskAsync($"{HeroesProfileApiEndpoint}/replays/fingerprints/{fingerprint}");
                }
                dynamic json = JObject.Parse(response);
                return (bool)json.exists;
            }
            catch (WebException ex) {
                if (await CheckApiThrottling(ex.Response)) {
                    return await CheckDuplicate(fingerprint);
                }
                _log.Warn(ex, $"Error checking fingerprint '{fingerprint}'");
                return false;
            }
        }

        /// <summary>
        /// Mass check replay fingerprints against database to detect duplicates
        /// </summary>
        /// <param name="fingerprints"></param>
        private async Task<string[]> CheckDuplicate(IEnumerable<string> fingerprints)
        {
            try {
                string response;
                using (var client = new WebClient()) {
                    response = await client.UploadStringTaskAsync($"{HeroesProfileApiEndpoint}/replays/fingerprints", String.Join("\n", fingerprints));
                }
                dynamic json = JObject.Parse(response);
                return (json.exists as JArray).Select(x => x.ToString()).ToArray();
            }
            catch (WebException ex) {
                if (await CheckApiThrottling(ex.Response)) {
                    return await CheckDuplicate(fingerprints);
                }
                _log.Warn(ex, $"Error checking fingerprint array");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Mass check replay fingerprints against database to detect duplicates
        /// </summary>
        public async Task CheckDuplicate(IEnumerable<ReplayFile> replays)
        {
            var exists = new HashSet<string>(await CheckDuplicate(replays.Select(x => x.Fingerprint)));
            replays.Where(x => exists.Contains(x.Fingerprint)).Map(x => x.UploadStatus = UploadStatus.Duplicate);
        }

        /// <summary>
        /// Check if Heroes Profile API request limit is reached and wait if it is
        /// </summary>
        /// <param name="response">Server response to examine</param>
        private async Task<bool> CheckApiThrottling(WebResponse response)
        {
            if (response != null && (int)(response as HttpWebResponse).StatusCode == 429) {
                _log.Warn($"Too many requests, waiting");
                await Task.Delay(10000);
                return true;
            } else {
                return false;
            }
        }
    }
}
