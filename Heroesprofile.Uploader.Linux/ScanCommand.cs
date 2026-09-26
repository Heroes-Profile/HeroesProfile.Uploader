using Heroesprofile.Uploader.Common;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// `scan --dry-run`: analyzes every replay under the resolved Accounts folder and reports what
    /// would happen on a `run`, without going anywhere near the upload endpoint or the replay storage
    /// file. Deliberately bypasses Manager - it never even constructs an IReplayStorage.
    /// </summary>
    internal static class ScanCommand
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // Same read-only dupe-check endpoint Uploader.cs hits before an upload.
        private const string FingerprintEndpoint = "https://www.heroesprofile.com/api/external/v1/replays/fingerprints/";

        public static async Task<int> Execute(string prefixOverride)
        {
            var config = AppConfig.Load();
            var prefix = PrefixSetup.ResolvePrefix(prefixOverride, config);
            PrefixSetup.Apply(prefix);

            _log.Info($"Scanning {ReplayLocation.Current} (dry run - no uploads, no storage writes)");

            var replayFiles = Directory.EnumerateFiles(ReplayLocation.Current, "*.StormReplay", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .ToList();

            var analyzer = new Analyzer();
            using var http = new HttpClient();

            var pending = 0;
            var duplicate = 0;
            var failed = 0;
            var filtered = new Dictionary<UploadStatus, int>();

            foreach (var path in replayFiles) {
                var name = Path.GetFileName(path);
                var file = new ReplayFile(path);

                var replay = analyzer.Analyze(file);

                // Matches Manager's own gate: a fresh ReplayFile starts at UploadStatus.None, and
                // Analyze() only overwrites that when GetPreStatus flags the replay (AI, PTR, too old...).
                if (replay == null || file.UploadStatus != UploadStatus.None) {
                    filtered[file.UploadStatus] = filtered.GetValueOrDefault(file.UploadStatus) + 1;
                    Console.WriteLine($"{name}: {file.UploadStatus} - not eligible, no dupe check done");
                    continue;
                }

                try {
                    var response = await http.GetAsync(FingerprintEndpoint + file.Fingerprint);
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) {
                        failed++;
                        Console.WriteLine($"{name}: fingerprint={file.Fingerprint} -> dupe check failed (HTTP {(int)response.StatusCode})");
                        continue;
                    }

                    var exists = (bool)JObject.Parse(body)["exists"];
                    if (exists) {
                        duplicate++;
                        Console.WriteLine($"{name}: fingerprint={file.Fingerprint} -> already on Heroes Profile, would be skipped");
                    } else {
                        pending++;
                        Console.WriteLine($"{name}: fingerprint={file.Fingerprint} -> WOULD UPLOAD");
                    }
                }
                catch (Exception ex) {
                    failed++;
                    Console.WriteLine($"{name}: fingerprint={file.Fingerprint} -> dupe check failed ({ex.Message})");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{replayFiles.Count} replay(s) scanned: {pending} would upload, {duplicate} already on Heroes Profile, " +
                $"{filtered.Values.Sum()} not eligible, {failed} dupe-check failure(s). Nothing was uploaded or written to disk.");
            foreach (var kv in filtered.OrderBy(kv => kv.Key.ToString())) {
                Console.WriteLine($"  {kv.Key}: {kv.Value}");
            }

            return 0;
        }
    }
}
