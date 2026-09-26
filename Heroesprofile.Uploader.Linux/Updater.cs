using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// Finds, downloads, verifies and stages Linux release updates, and applies a staged one. Used by
    /// both the GUI (auto-check at startup/hourly + the manual "Check for update" button, which then
    /// shows a restart banner) and `run` (a startup/24h check that only logs - no self-replacement
    /// headless, since there's no one to click "Restart now"). Entirely network/filesystem, no
    /// Avalonia dependency, so it's usable from the CLI-only build path too.
    /// </summary>
    internal sealed class Updater
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private const string Sha256SumsAssetName = "SHA256SUMS";

        // 0755 - the downloaded binary/AppImage needs its executable bit set; File.Copy/extraction
        // don't preserve that from a plain HTTP download the way File.Copy preserves it from disk.
        private const UnixFileMode ExecutableMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        private readonly string _apiBase;
        private readonly HttpClient _http;

        /// <param name="apiBase">
        /// GitHub API base ("https://api.github.com" by default) - a constructor parameter rather than
        /// a config key so tests can point it at a local fake server instead.
        /// </param>
        public Updater(string apiBase = "https://api.github.com")
        {
            _apiBase = apiBase.TrimEnd('/');
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("heroesprofile-uploader-linux");
        }

        public sealed class ReleaseInfo
        {
            public ReleaseVersion Version;
            public string TagName;
            public string HtmlUrl;
            public string AssetName;
            public string AssetUrl;
            public string Sha256SumsUrl;
        }

        public enum StageOutcome
        {
            /// <summary>Already on the newest eligible release (or nothing newer was found).</summary>
            NoUpdate,
            /// <summary>A newer release is downloaded, verified and staged as "&lt;target&gt;.new" - ready for "Restart now".</summary>
            Staged,
            /// <summary>A newer release exists but the target's directory isn't writable (e.g. /opt) - can't stage; show a "read the release page" banner instead.</summary>
            Fallback,
            /// <summary>Refused to stage - not a real single-file build (dotnet run/build output).</summary>
            Skipped,
            /// <summary>Download/verification failed - logged, safe to retry on the next check.</summary>
            Failed,
        }

        public sealed class StageResult
        {
            public StageOutcome Outcome;
            public ReleaseVersion Version;
            public string ReleaseUrl;
            public string Reason;

            public static StageResult NoUpdate(ReleaseVersion current) => new StageResult { Outcome = StageOutcome.NoUpdate, Version = current };
            public static StageResult Of(StageOutcome outcome, ReleaseInfo release, string reason = null) =>
                new StageResult { Outcome = outcome, Version = release?.Version, ReleaseUrl = release?.HtmlUrl, Reason = reason };
        }

        /// <summary>
        /// The .AppImage file this process was launched from, or null if it wasn't. The AppImage runtime
        /// sets $APPIMAGE and $APPDIR (its mount point) for the app, but child processes inherit both -
        /// so a tarball binary started from, say, a terminal that is itself an AppImage sees that other
        /// app's $APPIMAGE. Only trust it when this executable actually lives inside $APPDIR.
        /// </summary>
        public static string RunningAppImage
        {
            get {
                var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
                var appDir = Environment.GetEnvironmentVariable("APPDIR");
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(appImage) || string.IsNullOrEmpty(appDir) || string.IsNullOrEmpty(exe)) {
                    return null;
                }
                var mount = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDir)) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(exe).StartsWith(mount, StringComparison.Ordinal) ? appImage : null;
            }
        }

        /// <summary>Where a staged/applied update targets: the running AppImage if there is one, otherwise the running executable's own path.</summary>
        public static string ResolveTarget(out bool isAppImage)
        {
            var appImage = RunningAppImage;
            isAppImage = appImage != null;
            return appImage ?? Environment.ProcessPath;
        }

        /// <summary>
        /// Finds the newest release matching an asset selector (any that has a `.AppImage` asset, a
        /// `.tar.gz` asset, or - for the headless "just tell me if something's newer" check - either),
        /// skipping drafts, prereleases (unless <paramref name="allowPreReleases"/>), and anything
        /// missing a SHA256SUMS asset alongside the one we'd actually download.
        /// </summary>
        public async Task<ReleaseInfo> FindLatestAsync(string repo, bool allowPreReleases, bool? wantAppImage = null)
        {
            var json = await _http.GetStringAsync($"{_apiBase}/repos/{repo}/releases?per_page=30");
            var releases = JArray.Parse(json);

            ReleaseInfo best = null;
            foreach (var r in releases) {
                if ((bool?)r["draft"] == true) {
                    continue;
                }
                if (!allowPreReleases && (bool?)r["prerelease"] == true) {
                    continue;
                }

                var version = ReleaseVersion.Parse((string)r["tag_name"]);
                if (version == null || (best != null && version <= best.Version)) {
                    continue;
                }

                var assets = (r["assets"] as JArray) ?? new JArray();
                var sums = FindAsset(assets, name => string.Equals(name, Sha256SumsAssetName, StringComparison.OrdinalIgnoreCase));
                if (sums == null) {
                    continue;
                }

                var wanted = wantAppImage == true ? FindAsset(assets, IsAppImageAsset)
                    : wantAppImage == false ? FindAsset(assets, IsTarGzAsset)
                    : FindAsset(assets, n => IsAppImageAsset(n) || IsTarGzAsset(n));
                if (wanted == null) {
                    continue;
                }

                best = new ReleaseInfo {
                    Version = version,
                    TagName = (string)r["tag_name"],
                    HtmlUrl = (string)r["html_url"],
                    AssetName = (string)wanted["name"],
                    AssetUrl = (string)wanted["browser_download_url"],
                    Sha256SumsUrl = (string)sums["browser_download_url"],
                };
            }
            return best;
        }

        private static JToken FindAsset(JArray assets, Func<string, bool> match) =>
            assets.FirstOrDefault(a => match((string)a["name"] ?? ""));

        private static bool IsAppImageAsset(string name) => name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);
        private static bool IsTarGzAsset(string name) => name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Full check-and-stage: resolves the target, finds the newest eligible release for it, and -
        /// if it's newer than this build and not already staged - downloads, verifies and stages it.
        /// Never throws; failures come back as <see cref="StageOutcome.Failed"/>.
        /// </summary>
        public async Task<StageResult> CheckAndStageAsync(AppConfig config)
        {
            var current = ReleaseVersion.Current();
            try {
                var target = ResolveTarget(out var isAppImage);
                if (string.IsNullOrEmpty(target)) {
                    return new StageResult { Outcome = StageOutcome.Failed, Reason = "Could not determine the running executable's path." };
                }

                var release = await FindLatestAsync(config.UpdateRepository, config.AllowPreReleases, isAppImage);
                if (release == null || !(release.Version > current)) {
                    return StageResult.NoUpdate(current);
                }

                var newPath = target + ".new";
                var metaPath = newPath + ".sha256";
                if (File.Exists(newPath) && TryReadStagedMeta(metaPath, out var stagedHash, out var stagedVersion) &&
                    stagedVersion != null && !(release.Version > stagedVersion) && VerifyFile(newPath, stagedHash)) {
                    // Already staged this (or a newer) version in an earlier check - nothing to do.
                    return StageResult.Of(StageOutcome.Staged, release);
                }

                var whyNot = DesktopIntegration.WhyNotInstallable();
                if (whyNot != null) {
                    return StageResult.Of(StageOutcome.Skipped, release, whyNot);
                }

                if (!IsDirectoryWritable(Path.GetDirectoryName(target))) {
                    return StageResult.Of(StageOutcome.Fallback, release);
                }

                await DownloadAndStageAsync(release, target, isAppImage, newPath, metaPath);
                _log.Info($"Update {release.TagName} downloaded and verified - will apply on restart.");
                return StageResult.Of(StageOutcome.Staged, release);
            }
            catch (Exception ex) {
                _log.Warn(ex, "Update check/download failed - will retry on the next check.");
                return new StageResult { Outcome = StageOutcome.Failed, Reason = ex.Message, Version = current };
            }
        }

        private async Task DownloadAndStageAsync(ReleaseInfo release, string target, bool isAppImage, string newPath, string metaPath)
        {
            var partPath = target + ".new.part";
            try {
                var sums = await _http.GetStringAsync(release.Sha256SumsUrl);
                var expectedHash = ParseSha256Sums(sums, release.AssetName);
                if (expectedHash == null) {
                    throw new InvalidOperationException($"SHA256SUMS has no entry for {release.AssetName}.");
                }

                using (var response = await _http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead)) {
                    response.EnsureSuccessStatusCode();
                    using var body = await response.Content.ReadAsStreamAsync();
                    using var file = new FileStream(partPath, FileMode.Create, FileAccess.Write);
                    await body.CopyToAsync(file);
                }

                if (!VerifyFile(partPath, expectedHash)) {
                    throw new InvalidOperationException($"Downloaded {release.AssetName} does not match SHA256SUMS.");
                }

                // SetUnixFileMode is a Linux/Unix-only API (CA1416) - fine, this whole project only ever runs on Linux.
#pragma warning disable CA1416
                if (isAppImage) {
                    // The AppImage asset *is* the target file - just make it executable and put it in place.
                    File.SetUnixFileMode(partPath, ExecutableMode);
                    File.Move(partPath, newPath, overwrite: true);
                } else {
                    // The tar.gz asset wraps the binary (HeroesProfileUploader/heroesprofile-uploader) -
                    // extract just that, verified, and discard the archive.
                    ExtractBinaryFromTarGz(partPath, newPath);
                    File.SetUnixFileMode(newPath, ExecutableMode);
                    File.Delete(partPath);
                }
#pragma warning restore CA1416

                // The sidecar records the *staged binary's* hash (post-extraction for tar.gz, so it
                // doesn't match SHA256SUMS's tar.gz entry) plus the release version, so a later check
                // can tell whether an existing .new is still current without re-downloading, and
                // apply-at-startup has something to verify against without needing network access.
                WriteStagedMeta(metaPath, ComputeSha256(newPath), release.Version);
            }
            catch {
                TryDelete(partPath);
                TryDelete(newPath);
                TryDelete(metaPath);
                throw;
            }
        }

        private static void ExtractBinaryFromTarGz(string tarGzPath, string destPath)
        {
            using (var fileStream = File.OpenRead(tarGzPath))
            using (var gzip = new GZipStream(fileStream, CompressionMode.Decompress))
            using (var reader = new TarReader(gzip)) {
                TarEntry entry;
                while ((entry = reader.GetNextEntry()) != null) {
                    if (entry.EntryType == TarEntryType.RegularFile && Path.GetFileName(entry.Name) == "heroesprofile-uploader") {
                        entry.ExtractToFile(destPath, overwrite: true);
                        return;
                    }
                }
            }
            throw new InvalidOperationException("heroesprofile-uploader binary not found inside the downloaded tar.gz.");
        }

        private static string ParseSha256Sums(string content, string assetName)
        {
            foreach (var rawLine in content.Split('\n')) {
                var line = rawLine.Trim();
                if (line.Length == 0) {
                    continue;
                }
                var parts = line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) {
                    continue;
                }
                // `sha256sum` prefixes the name with "*" when it hashed in binary mode - strip it.
                var name = parts[1].TrimStart('*');
                if (string.Equals(name, assetName, StringComparison.Ordinal)) {
                    return parts[0].Trim();
                }
            }
            return null;
        }

        private static bool IsDirectoryWritable(string dir)
        {
            try {
                var probe = Path.Combine(dir, $".heroesprofile-uploader-writetest-{Guid.NewGuid():N}");
                using (File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch {
                return false;
            }
        }

        /// <summary>
        /// Stages a local binary as "&lt;target&gt;.new", the same way a downloaded update is staged, so
        /// the next launch of <paramref name="target"/> applies it. Used when the target can't be
        /// replaced right now because it's running. Does nothing if a newer version is already staged.
        /// </summary>
        public static void StageLocalCopy(string sourcePath, string target, ReleaseVersion version)
        {
            var newPath = target + ".new";
            var metaPath = newPath + ".sha256";
            if (File.Exists(newPath) && TryReadStagedMeta(metaPath, out var stagedHash, out var stagedVersion) &&
                stagedVersion != null && !(version > stagedVersion) && VerifyFile(newPath, stagedHash)) {
                return;
            }
            try {
                File.Copy(sourcePath, newPath, overwrite: true);
                WriteStagedMeta(metaPath, ComputeSha256(newPath), version);
            }
            catch {
                TryDelete(newPath);
                TryDelete(metaPath);
                throw;
            }
        }

        private static void WriteStagedMeta(string metaPath, string hash, ReleaseVersion version) =>
            File.WriteAllText(metaPath, $"{hash}\n{version}\n");

        /// <summary>Reads a "&lt;target&gt;.new.sha256" sidecar. Line 1 is the hash apply-at-startup
        /// needs; line 2 (the staged release's version) is only used by CheckAndStageAsync to decide
        /// whether an existing .new is stale.</summary>
        private static bool TryReadStagedMeta(string metaPath, out string hash, out ReleaseVersion version)
        {
            hash = null;
            version = null;
            if (!File.Exists(metaPath)) {
                return false;
            }
            try {
                var lines = File.ReadAllLines(metaPath);
                hash = lines.Length > 0 ? lines[0].Trim() : null;
                version = lines.Length > 1 ? ReleaseVersion.Parse(lines[1]) : null;
                return !string.IsNullOrEmpty(hash);
            }
            catch {
                return false;
            }
        }

        private static bool VerifyFile(string path, string expectedHexHash) =>
            !string.IsNullOrEmpty(expectedHexHash) && string.Equals(ComputeSha256(path), expectedHexHash, StringComparison.OrdinalIgnoreCase);

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
            catch (Exception ex) {
                _log.Debug(ex, $"Could not clean up {path}");
            }
        }

        /// <summary>
        /// Applies a staged "&lt;target&gt;.new" over the running target and launches the new binary -
        /// the GUI's "Restart now". Caller is expected to shut the current process down right after a
        /// successful launch. Returns false (does nothing) if there's nothing valid staged.
        /// </summary>
        public static bool ApplyStagedAndRelaunch(bool minimized)
        {
            var target = ResolveTarget(out _);
            if (string.IsNullOrEmpty(target)) {
                return false;
            }
            var childArgs = minimized ? new[] { "--minimized" } : Array.Empty<string>();
            if (!ApplyStagedInPlace(target, childArgs, out var appliedError)) {
                if (appliedError != null) {
                    _log.Warn($"Restart now: {appliedError}");
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Very-early-in-Main check: if a verified update is staged for the running executable, applies
        /// it and re-execs with the same args, so this launch actually runs the new binary instead of
        /// whatever was already loaded into memory. Guarded by $HP_UPDATER_APPLIED so a relaunch never
        /// loops. Returns true if it relaunched (caller should exit immediately without doing anything else).
        /// </summary>
        public static bool TryApplyAtStartup(string[] args)
        {
            if (Environment.GetEnvironmentVariable("HP_UPDATER_APPLIED") == "1") {
                return false;
            }

            var target = ResolveTarget(out _);
            if (string.IsNullOrEmpty(target) || !File.Exists(target + ".new")) {
                return false;
            }

            if (!ApplyStagedInPlace(target, args, out var error)) {
                if (error != null) {
                    _log.Warn($"Staged update: {error}");
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Verifies "&lt;target&gt;.new" against its sidecar hash and, if it checks out, launches a tiny
        /// detached "/bin/sh" that does the actual swap-and-relaunch, then returns immediately - the
        /// caller is expected to exit right after (nothing more runs in this process on the applied path).
        /// The rename+relaunch is done by the shell rather than here because a self-contained single-file
        /// build doesn't load every bundled assembly upfront - it lazily reads each one off its own
        /// backing file, by path, the first time something needs it. Renaming a new file over that same
        /// path (which is exactly what applying a staged update does) is fine for the kernel - this
        /// process keeps its existing mappings via the old inode - but any *not yet touched* assembly it
        /// tries to lazily load *after* the swap fails, since the path now serves different bytes
        /// (confirmed experimentally: an untouched process that swaps its own file immediately fails
        /// loading System.Collections; one that's touched more of the BCL by then fails later, e.g. inside
        /// Process.Start itself). A plain shell has nothing left to lazily load, so it sidesteps the
        /// problem entirely instead of needing a throwaway warm-up call to dodge it.
        /// The script only ever uses positional parameters ($1, $2, "$@") - never string-interpolate a
        /// path or arg into the script text itself. If the "mv" fails (e.g. a permissions change), the
        /// script still execs the (unmoved) target, so the app keeps working on the old version.
        /// A failed/missing verification, or a staged version no newer than this build, discards the
        /// staged files and launches nothing.
        /// </summary>
        private static bool ApplyStagedInPlace(string target, string[] childArgs, out string error)
        {
            error = null;
            var newPath = target + ".new";
            var metaPath = newPath + ".sha256";

            if (!TryReadStagedMeta(metaPath, out var hash, out var stagedVersion) || !VerifyFile(newPath, hash)) {
                error = "staged update failed verification - discarding it.";
                TryDelete(newPath);
                TryDelete(metaPath);
                return false;
            }

            // Left over from before the target was replaced some other way (e.g. `install` of a newer build).
            if (stagedVersion != null && !(stagedVersion > ReleaseVersion.Current())) {
                error = $"staged update {stagedVersion} is not newer than this build - discarding it.";
                TryDelete(newPath);
                TryDelete(metaPath);
                return false;
            }

            // $1=new $2=target, then the target's own args after `shift 2`. A short sleep gives this
            // .NET process time to actually exit (it does, right after Process.Start below) before the
            // new binary starts - there's no single-instance lock to fight over, but overlapping the
            // rolling log file for longer than necessary isn't worth it either.
            const string script =
                "new=$1; target=$2; shift 2; sleep 0.5; " +
                "mv -f -- \"$new\" \"$target\" && rm -f -- \"$new.sha256\"; " +
                "exec \"$target\" \"$@\"";

            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add("sh"); // becomes $0 inside the script, not used
            psi.ArgumentList.Add(newPath);
            psi.ArgumentList.Add(target);
            foreach (var a in childArgs) {
                psi.ArgumentList.Add(a);
            }
            psi.Environment["HP_UPDATER_APPLIED"] = "1";

            try {
                Process.Start(psi);
            }
            catch (Exception ex) {
                error = $"could not launch the update-apply step: {ex.Message}";
                return false;
            }
            return true;
        }
    }
}
