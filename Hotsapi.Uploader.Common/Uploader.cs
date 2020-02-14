using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hotsapi.Uploader.Common
{
    public class Uploader : IUploader
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
#if DEBUG
        const string ApiEndpoint = "https://api.heroesprofile.com/api";
#else
        const string ApiEndpoint = "https://api.heroesprofile.com/api";
#endif

        public bool UploadToHotslogs { get; set; }

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
        public async Task Upload(ReplayFile file)
        {
            file.UploadStatus = UploadStatus.InProgress;
            if (file.Fingerprint != null && await CheckDuplicate(file.Fingerprint)) {
                _log.Debug($"File {file} marked as duplicate");
                file.UploadStatus = UploadStatus.Duplicate;
            } else {
                file.UploadStatus = await Upload(file.Fingerprint, file.Filename);
            }
        }

        /// <summary>
        /// Upload replay
        /// </summary>
        /// <param name="file">Path to file</param>
        /// <returns>Upload result</returns>
        public async Task<UploadStatus> Upload(string fingerprint, string file)
        {
            try {
                string response;
                using (var client = new WebClient()) {
                    var bytes = await client.UploadFileTaskAsync($"{ApiEndpoint}/upload?fingerprint={fingerprint}", file);
                    response = Encoding.UTF8.GetString(bytes);
                }
                dynamic json = JObject.Parse(response);
                if ((bool)json.success) {
                    if (Enum.TryParse<UploadStatus>((string)json.status, out UploadStatus status)) {
                        _log.Debug($"Uploaded file '{file}': {status}");
                        return status;
                    } else {
                        _log.Error($"Unknown upload status '{file}': {json.status}");
                        return UploadStatus.UploadError;
                    }
                } else {
                    _log.Warn($"Error uploading file '{file}': {response}");
                    return UploadStatus.UploadError;
                }
            }
            catch (WebException ex) {
                if (await CheckApiThrottling(ex.Response)) {
                    return await Upload(fingerprint, file);
                }
                _log.Warn(ex, $"Error uploading file '{file}'");
                return UploadStatus.UploadError;
            }
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
                    response = await client.DownloadStringTaskAsync($"{ApiEndpoint}/replays/fingerprints/{fingerprint}");
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
                    response = await client.UploadStringTaskAsync($"{ApiEndpoint}/replays/fingerprints", String.Join("\n", fingerprints));
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
        /// Get minimum HotS client build supported by HotsApi
        /// </summary>
        public async Task<int> GetMinimumBuild()
        {
            //We likely want to track which replays arn't supported by HotsApi so that we don't send them to HotsApi, 
            //but I would like to change this so that it doesn't prevent replays uploading to our own storage, as we can support any replay build


            try {
                using (var client = new WebClient()) {
                    var response = await client.DownloadStringTaskAsync($"{ApiEndpoint}/replays/hotsapi-min-build");
                    if (!int.TryParse(response, out int build)) {
                        _log.Warn($"Error parsing minimum build: {response}");
                        return 0;
                    }
                    return build;
                }
            }
            catch (WebException ex) {
                if (await CheckApiThrottling(ex.Response)) {
                    return await GetMinimumBuild();
                }
                _log.Warn(ex, $"Error getting minimum build");
                return 0;
            }
        }

        /// <summary>
        /// Check if Hotsapi request limit is reached and wait if it is
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
