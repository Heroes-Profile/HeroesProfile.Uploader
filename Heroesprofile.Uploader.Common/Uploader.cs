using Newtonsoft.Json.Linq;
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
        // v1, served from the main site. api.heroesprofile.com keeps answering the
        // old paths for already-deployed clients, but it is not a base URL to build
        // against — once DNS moves, everything on it except those aliases redirects
        // here. The upload routes stay keyless, so there is still no token to send.
#if DEBUG
        const string HeroesProfileApiEndpoint = "http://127.0.0.1:8000/api/external/v1";
        const string HeroesProfileMatchParsed = "http://127.0.0.1:8000/api/external/v1/replays/parsed?replayID=";
        const string HeroesProfileMatchSummary = "http://localhost/Match/Single/?replayID=";



#else
        const string HeroesProfileApiEndpoint = "https://www.heroesprofile.com/api/external/v1";
        const string HeroesProfileMatchParsed = "https://www.heroesprofile.com/api/external/v1/replays/parsed?replayID=";
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
                    var fileAge = DateTime.Now - File.GetLastWriteTime(file);
                    _log.Debug($"Postmatch check: replayID={replayID}, PostMatchPage={PostMatchPage}, fileAge={fileAge.TotalSeconds:F1}s");
                    if (fileAge <= TimeSpan.FromSeconds(60) && PostMatchPage && replayID != 0) {
                        await postMatchAnalysis(replayID);
                    }
                }
                catch (Exception ex) {
                    _log.Error(ex, "Postmatch failed");
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
            var parsedUrl = $"{HeroesProfileMatchParsed}{replayID}";
            var timer = new Stopwatch();
            timer.Start();
            var checks = 0;
            var lastResponse = "none";
            while (timer.ElapsedMilliseconds < 15000) {
                checks++;
                try {
                    string response;
                    using (var client = new WebClient()) {
                        response = await client.DownloadStringTaskAsync(parsedUrl);
                    }
                    lastResponse = Describe(response);
                    if (response?.Trim() == "true") {
                        timer.Stop();
                        var pageUrl = $"{HeroesProfileMatchSummary}{replayID}";
                        _log.Debug($"Replay {replayID} parsed after {timer.ElapsedMilliseconds}ms, opening postmatch page {pageUrl}");
                        try {
                            // UseShellExecute is needed to open a URL rather than try to execute it as a file -
                            // it's the .NET Framework default but not on .NET Core, where it also maps to xdg-open on Linux
                            Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true });
                        }
                        catch (Exception ex) {
                            _log.Error(ex, $"Failed to open postmatch page {pageUrl}");
                        }
                        WebhookNotifier.Notify("postmatch", pageUrl);
                        return;
                    }
                }
                catch (WebException ex) {
                    var status = (ex.Response as HttpWebResponse)?.StatusCode;
                    lastResponse = status != null ? $"HTTP {(int)status}" : $"{ex.Status}";
                    _log.Warn(ex, $"Parsed check for replay {replayID} failed ({lastResponse})");
                    if (ex.Response != null) {
                        await CheckApiThrottling(ex.Response);
                    }
                }
                await Task.Delay(1000);
            }
            timer.Stop();
            _log.Warn($"Replay {replayID} was not parsed after {checks} checks over {timer.ElapsedMilliseconds}ms (last response: {lastResponse}), postmatch page not opened");
        }
        /// <summary>
        /// Render a response body for the log: an error page is worth seeing, but not all 40kb of it
        /// </summary>
        private static string Describe(string response)
        {
            if (response == null) {
                return "<null>";
            }
            if (response.Length == 0) {
                return "<empty>";
            }
            var oneLine = response.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length > 500 ? $"{oneLine.Substring(0, 500)}... ({response.Length} chars total)" : oneLine;
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
