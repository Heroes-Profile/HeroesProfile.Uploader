using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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

        // One client for the app's lifetime, as HttpClient is meant to be used (a new one per request
        // can run out of sockets). Same 100s timeout WebClient had.
        private static readonly HttpClient _sharedClient = new HttpClient();

        private readonly HttpClient _client;
        private readonly TimeSpan _throttleDelay;

        /// <summary>How long to keep asking whether the site has parsed an upload before giving up on the postmatch page.</summary>
        internal TimeSpan PostMatchWaitTime { get; set; } = TimeSpan.FromSeconds(60);
        internal TimeSpan PostMatchPollInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// New instance of replay uploader
        /// </summary>
        public Uploader() : this(_sharedClient, TimeSpan.FromSeconds(10))
        {
        }

        /// <param name="client">Where requests go - tests pass one with a fake handler.</param>
        /// <param name="throttleDelay">How long to wait before retrying when the API says "too many requests" (429).</param>
        internal Uploader(HttpClient client, TimeSpan throttleDelay)
        {
            _client = client;
            _throttleDelay = throttleDelay;
        }

        /// <summary>
        /// Upload replay
        /// </summary>
        /// <param name="file"></param>
        public async Task Upload(Replay replay_results, ReplayFile file, bool PostMatchPage)
        {
            file.UploadStatus = UploadStatus.InProgress;
            var duplicate = file.Fingerprint != null ? await CheckDuplicate(file.Fingerprint) : (Exists: false, ReplayId: 0);
            if (duplicate.Exists) {
                _log.Debug($"File {file} marked as duplicate");
                SetReplayId(file, duplicate.ReplayId);
                file.UploadStatus = UploadStatus.Duplicate;
            } else {
                var (status, replayId) = await UploadFile(replay_results, file.Fingerprint, file.Filename, PostMatchPage);
                // Before the status: the status change is what the list refreshes on
                SetReplayId(file, replayId);
                file.UploadStatus = status;
            }
        }

        // An id of 0 means the server didn't send one (an older server's duplicate check) - keep what's known.
        private static void SetReplayId(ReplayFile file, int replayId)
        {
            if (replayId > 0) {
                file.ReplayId = replayId;
            }
        }

        /// <summary>
        /// Upload replay
        /// </summary>
        /// <param name="file">Path to file</param>
        /// <returns>Upload result</returns>
        public async Task<UploadStatus> Upload(Replay replay_results, string fingerprint, string file, bool PostMatchPage) =>
            (await UploadFile(replay_results, fingerprint, file, PostMatchPage)).Status;

        /// <summary>Uploads the replay; its upload status and its id on Heroes Profile (0 if there isn't one).</summary>
        private async Task<(UploadStatus Status, int ReplayId)> UploadFile(Replay replay_results, string fingerprint, string file, bool PostMatchPage)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            AssemblyName assemblyName = assembly.GetName();
            Version assemblyVersion = assemblyName.Version;

            try {
                string response;
                // A multipart/form-data POST with the replay as its "file" part - what WebClient.UploadFile sent.
                using (var stream = File.OpenRead(file))
                using (var fileContent = new StreamContent(stream))
                using (var form = new MultipartFormDataContent()) {
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    form.Add(fileContent, "file", Path.GetFileName(file));
                    using (var reply = await _client.PostAsync($"{HeroesProfileApiEndpoint}/upload/heroesprofile/desktop?fingerprint={fingerprint}&version={assemblyVersion}", form)) {
                        if (!reply.IsSuccessStatusCode) {
                            if (await WaitIfThrottled(reply.StatusCode)) {
                                return await UploadFile(replay_results, fingerprint, file, PostMatchPage);
                            }
                            _log.Warn($"Error uploading file '{file}': HTTP {(int)reply.StatusCode} {Describe(await reply.Content.ReadAsStringAsync())}");
                            return (UploadStatus.UploadError, 0);
                        }
                        response = await reply.Content.ReadAsStringAsync();
                    }
                }

                UploadResult result = UploadResult.FromJson(response);

                try {
                    int replayID = result.ReplayId;
                    var fileAge = DateTime.Now - File.GetLastWriteTime(file);
                    _log.Debug($"Postmatch check: replayID={replayID}, PostMatchPage={PostMatchPage}, fileAge={fileAge.TotalSeconds:F1}s");
                    if (fileAge <= TimeSpan.FromSeconds(60) && PostMatchPage && replayID != 0) {
                        // In the background: the site can take a while to parse the replay, and the
                        // upload queue shouldn't wait on it
                        Task.Run(async () => {
                            try {
                                await postMatchAnalysis(replayID);
                            }
                            catch (Exception ex) {
                                _log.Error(ex, "Postmatch failed");
                            }
                        }).Forget();
                    }
                }
                catch (Exception ex) {
                    _log.Error(ex, "Postmatch failed");
                }


                if (!string.IsNullOrEmpty(result.Status)) {
                    if (Enum.TryParse<UploadStatus>((string)result.Status, out UploadStatus status)) {
                        _log.Debug($"Uploaded file '{file}': {status}");
                        return (status, result.ReplayId);
                    } else {
                        _log.Error($"Unknown upload status '{file}': {result.Status}");
                        return (UploadStatus.UploadError, 0);
                    }
                } else {
                    _log.Warn($"Error uploading file '{file}': {response}");
                    return (UploadStatus.UploadError, 0);
                }


            }
            catch (Exception ex) when (IsNetworkFailure(ex)) {
                _log.Warn(ex, $"Error uploading file '{file}'");
                return (UploadStatus.UploadError, 0);
            }
        }

        private async Task postMatchAnalysis(int replayID)
        {
            var parsedUrl = $"{HeroesProfileMatchParsed}{replayID}";
            var timer = new Stopwatch();
            timer.Start();
            var checks = 0;
            var lastResponse = "none";
            while (timer.Elapsed < PostMatchWaitTime) {
                checks++;
                try {
                    using (var reply = await _client.GetAsync(parsedUrl)) {
                        if (reply.IsSuccessStatusCode) {
                            var response = await reply.Content.ReadAsStringAsync();
                            lastResponse = Describe(response);
                            if (response?.Trim() == "true") {
                                timer.Stop();
                                var pageUrl = $"{HeroesProfileMatchSummary}{replayID}";
                                _log.Debug($"Replay {replayID} parsed after {timer.ElapsedMilliseconds}ms, opening postmatch page {pageUrl}");
                                try {
                                    // UseShellExecute is needed to open a URL rather than try to execute it as a file
                                    // (it also maps to xdg-open on Linux and `open` on macOS)
                                    Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true });
                                }
                                catch (Exception ex) {
                                    _log.Error(ex, $"Failed to open postmatch page {pageUrl}");
                                }
                                WebhookNotifier.Notify("postmatch", pageUrl);
                                return;
                            }
                        } else {
                            lastResponse = $"HTTP {(int)reply.StatusCode}";
                            _log.Warn($"Parsed check for replay {replayID} failed ({lastResponse})");
                            await WaitIfThrottled(reply.StatusCode);
                        }
                    }
                }
                catch (Exception ex) when (IsNetworkFailure(ex)) {
                    lastResponse = ex is HttpRequestException http ? $"{http.HttpRequestError}" : "Timeout";
                    _log.Warn(ex, $"Parsed check for replay {replayID} failed ({lastResponse})");
                }
                await Task.Delay(PostMatchPollInterval);
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
        /// Check replay fingerprint against database to detect duplicate. The server also sends the
        /// replay's id when it has it (its "replayID" field - 0 here when it's missing or null).
        /// </summary>
        /// <param name="fingerprint"></param>
        private async Task<(bool Exists, int ReplayId)> CheckDuplicate(string fingerprint)
        {
            try {
                using (var reply = await _client.GetAsync($"{HeroesProfileApiEndpoint}/replays/fingerprints/{fingerprint}")) {
                    if (!reply.IsSuccessStatusCode) {
                        if (await WaitIfThrottled(reply.StatusCode)) {
                            return await CheckDuplicate(fingerprint);
                        }
                        _log.Warn($"Error checking fingerprint '{fingerprint}': HTTP {(int)reply.StatusCode}");
                        return (false, 0);
                    }
                    var json = JObject.Parse(await reply.Content.ReadAsStringAsync());
                    var replayId = json["replayID"]?.Type == JTokenType.Integer ? (int)json["replayID"] : 0;
                    return ((bool)json["exists"], replayId);
                }
            }
            catch (Exception ex) when (IsNetworkFailure(ex)) {
                _log.Warn(ex, $"Error checking fingerprint '{fingerprint}'");
                return (false, 0);
            }
        }

        /// <summary>
        /// Mass check replay fingerprints against database to detect duplicates
        /// </summary>
        /// <param name="fingerprints"></param>
        private async Task<string[]> CheckDuplicate(IEnumerable<string> fingerprints)
        {
            try {
                using (var body = new StringContent(String.Join("\n", fingerprints), Encoding.UTF8))
                using (var reply = await _client.PostAsync($"{HeroesProfileApiEndpoint}/replays/fingerprints", body)) {
                    if (!reply.IsSuccessStatusCode) {
                        if (await WaitIfThrottled(reply.StatusCode)) {
                            return await CheckDuplicate(fingerprints);
                        }
                        _log.Warn($"Error checking fingerprint array: HTTP {(int)reply.StatusCode}");
                        return Array.Empty<string>();
                    }
                    var json = JObject.Parse(await reply.Content.ReadAsStringAsync());
                    return (json["exists"] as JArray).Select(x => x.ToString()).ToArray();
                }
            }
            catch (Exception ex) when (IsNetworkFailure(ex)) {
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
        /// <param name="status">Status code of the server's response</param>
        /// <returns>true if it was a "too many requests" response and the caller should retry</returns>
        private async Task<bool> WaitIfThrottled(HttpStatusCode status)
        {
            if (status == HttpStatusCode.TooManyRequests) {
                _log.Warn($"Too many requests, waiting");
                await Task.Delay(_throttleDelay);
                return true;
            } else {
                return false;
            }
        }

        /// <summary>
        /// The failures WebClient reported as a WebException without a response: no connection, DNS,
        /// TLS, a dropped connection - and a request that timed out.
        /// </summary>
        private static bool IsNetworkFailure(Exception ex) =>
            ex is HttpRequestException || (ex is TaskCanceledException && ex.InnerException is TimeoutException);
    }
}
