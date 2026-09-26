using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;

namespace Heroesprofile.Uploader.Desktop.Platform
{
    /// <summary>
    /// Writes/removes the Linux desktop-integration files: the app-menu .desktop entry ("Show in app
    /// menu", or the `install`/`uninstall` CLI commands) and the autostart entry ("Start on login").
    /// Run as the AppImage, both point at the .AppImage file itself, which Velopack updates in place.
    /// Run as the plain tarball binary, `install` copies it to ~/.local/bin first, so the menu entry
    /// and the systemd service have a fixed place to run it from.
    /// </summary>
    [SupportedOSPlatform("linux")]
    internal static class DesktopIntegration
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private const string AppId = "heroesprofile-uploader";
        private const string IconResourceName = "heroesprofile-uploader-icon.png";

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static string XdgHome(string envVar, string fallbackLeaf)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            return !string.IsNullOrWhiteSpace(value) ? value : Path.Combine(Home, fallbackLeaf);
        }

        // ~/.local/bin has no XDG variable of its own - it's the de facto convention every major
        // distro's default $PATH (and tools like `pip install --user`) already agree on.
        private static string BinDir => Path.Combine(Home, ".local", "bin");
        private static string DataHome => XdgHome("XDG_DATA_HOME", Path.Combine(".local", "share"));
        private static string ConfigHome => XdgHome("XDG_CONFIG_HOME", ".config");

        public static string InstalledExePath => Path.Combine(BinDir, AppId);

        /// <summary>
        /// The .AppImage file this process runs from, or null when it isn't one. $APPIMAGE alone isn't
        /// proof: child processes inherit it, so a tarball binary started from inside some *other*
        /// AppImage (a terminal or IDE shipped as one) sees that app's path. Only trusted when this
        /// process's executable really is inside the AppImage's mount ($APPDIR).
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

        /// <summary>
        /// Where this app can be launched from again later. Inside an AppImage, ProcessPath is the
        /// binary in a temporary mount that disappears on exit, so the .AppImage file itself is used.
        /// </summary>
        private static string LaunchablePath => RunningAppImage ?? Environment.ProcessPath;

        public static bool IsAppMenuEntryInstalled => File.Exists(DesktopEntryPath);
        private static string DesktopEntryPath => Path.Combine(DataHome, "applications", $"{AppId}.desktop");
        private static string AutostartEntryPath => Path.Combine(ConfigHome, "autostart", $"{AppId}.desktop");
        // Actual asset is 486x432; this is the closest standard hicolor bucket, and desktop
        // environments scale it down for the menu/taskbar without visible loss.
        private static string IconPath => Path.Combine(DataHome, "icons", "hicolor", "512x512", "apps", $"{AppId}.png");

        /// <summary>
        /// Null if the running executable can be installed, otherwise why not. `dotnet run`/`dotnet build`
        /// output is an apphost with the managed .dll (and the rest of the app) next to it, so copying
        /// the apphost alone would be broken. The single-file publish has no such .dll. Inside an
        /// AppImage the running executable is that same single-file binary, so it installs as a plain
        /// executable that doesn't need the AppImage (or FUSE) afterwards.
        /// </summary>
        public static string WhyNotInstallable()
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || exe.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.Combine(Path.GetDirectoryName(exe), Path.GetFileName(exe) + ".dll"))) {
                return "install needs to be run from the published single-file binary, not `dotnet run`/`dotnet build` output. " +
                    "Publish with `dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true` and run that.";
            }
            return null;
        }

        /// <summary>
        /// Adds the app-menu entry and icon. As the AppImage, the entry runs the .AppImage file (which
        /// Velopack keeps up to date); otherwise the running binary is copied to ~/.local/bin first.
        /// </summary>
        public static void InstallAppMenuEntry()
        {
            var launch = RunningAppImage;
            if (launch == null) {
                var sourceExePath = Environment.ProcessPath;
                Directory.CreateDirectory(BinDir);
                // Already running the installed copy: just (re)write the menu entry and icon.
                if (Path.GetFullPath(sourceExePath) != Path.GetFullPath(InstalledExePath)) {
                    PlaceInstalledCopy(sourceExePath);
                } else {
                    CancelPendingDelete();
                }
                launch = InstalledExePath;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName))
            using (var dest = File.Create(IconPath)) {
                resource.CopyTo(dest);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DesktopEntryPath));
            File.WriteAllText(DesktopEntryPath, DesktopEntryContents(launch, minimized: false));
            _log.Info($"Wrote app menu entry {DesktopEntryPath}");
        }

        /// <summary>Removes everything <see cref="InstallAppMenuEntry"/> added. Leaves config and replay data alone.</summary>
        public static void UninstallAppMenuEntry()
        {
            RemoveAppMenuEntry();
            // Uninstalling should also turn off start-on-login - an autostart entry pointing at a
            // binary that no longer exists would just silently fail every login.
            SetStartOnLogin(false);
        }

        /// <summary>Removes the app-menu entry, icon and any ~/.local/bin copy, but not the autostart entry.</summary>
        public static void RemoveAppMenuEntry()
        {
            DeleteIfExists(DesktopEntryPath);
            DeleteIfExists(IconPath);
            if (IsRunningInstalledCopy) {
                DeleteAfterExit(InstalledExePath);
            } else {
                DeleteIfExists(InstalledExePath);
            }
        }

        /// <summary>True when this process is the ~/.local/bin copy.</summary>
        public static bool IsRunningInstalledCopy =>
            !string.IsNullOrEmpty(Environment.ProcessPath) && PathsEqual(Environment.ProcessPath, InstalledExePath);

        private static Process _pendingDelete;

        /// <summary>
        /// Deleting the running binary would crash this process later - a single-file build loads parts
        /// of itself from its own path as it goes - so a detached shell deletes it once this process exits.
        /// </summary>
        private static void DeleteAfterExit(string path)
        {
            CancelPendingDelete();
            // $1=our pid, $2=path. Positional parameters only - never interpolate the path into the script.
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("while kill -0 \"$1\" 2>/dev/null; do sleep 1; done; rm -f -- \"$2\"");
            psi.ArgumentList.Add("sh");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add(path);
            _pendingDelete = Process.Start(psi);
            _log.Info($"Will remove {path} when this instance exits");
        }

        // "Show in app menu" turned back on before exiting - keep the binary after all.
        private static void CancelPendingDelete()
        {
            if (_pendingDelete == null) {
                return;
            }
            try {
                if (!_pendingDelete.HasExited) {
                    _pendingDelete.Kill();
                }
            }
            catch (Exception ex) {
                _log.Debug(ex, "Could not cancel the pending delete");
            }
            _pendingDelete = null;
        }

        /// <summary>Writes or removes the XDG autostart entry for Settings' "Start on login" toggle.</summary>
        public static void SetStartOnLogin(bool enabled)
        {
            if (!enabled) {
                DeleteIfExists(AutostartEntryPath);
                return;
            }

            // The AppImage if that's what's running (Velopack keeps it current); otherwise prefer the
            // installed copy so autostart survives the source binary moving, falling back to wherever
            // we're currently running from (e.g. testing before `install`).
            var exePath = RunningAppImage
                ?? (File.Exists(InstalledExePath) && _pendingDelete == null ? InstalledExePath : Environment.ProcessPath);
            Directory.CreateDirectory(Path.GetDirectoryName(AutostartEntryPath));
            File.WriteAllText(AutostartEntryPath, DesktopEntryContents(exePath, minimized: true));
            _log.Info($"Wrote autostart entry {AutostartEntryPath}");
        }

        private static string DesktopEntryContents(string exePath, bool minimized)
        {
            var exec = minimized ? $"\"{exePath}\" --minimized" : $"\"{exePath}\"";
            return
$@"[Desktop Entry]
Type=Application
Name=Heroes Profile Uploader
Comment=Upload Heroes of the Storm replays to heroesprofile.com
Exec={exec}
Icon={AppId}
Terminal=false
Categories=Game;Utility;
StartupWMClass={AppId}
";
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) {
                File.Delete(path);
                _log.Info($"Removed {path}");
            }
        }

        /// <summary>
        /// Tarball installs: if the app-menu entry is installed and this running binary is newer than
        /// the ~/.local/bin copy, re-copies it - the same thing `install` does, minus rewriting the
        /// desktop entry. Called at GUI and `run` startup so someone who replaced the tarball binary
        /// ends up running the new version from the menu and the systemd service too. Not needed for
        /// the AppImage, which Velopack updates in place, nor under `dotnet run`/`dotnet build` output.
        /// </summary>
        public static void RefreshInstalledCopyIfStale()
        {
            if (!IsAppMenuEntryInstalled || RunningAppImage != null || WhyNotInstallable() != null) {
                return;
            }

            var running = Environment.ProcessPath;
            if (string.IsNullOrEmpty(running) || PathsEqual(running, InstalledExePath)) {
                return; // already running the installed copy
            }

            // Best effort - a failure here must never stop this (newer) binary from starting.
            try {
                if (IsRunningNewerThanInstalled(running)) {
                    PlaceInstalledCopy(running);
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, $"Could not refresh {InstalledExePath}");
            }
        }

        /// <summary>
        /// Puts a copy of <paramref name="source"/> at ~/.local/bin. If that copy is running (e.g. from
        /// Start on login or the systemd service), Linux refuses to write to it ("Text file busy"), and
        /// renaming over it would break it - single-file builds lazily load assemblies from their own
        /// path - so it's left alone and refreshed the next time this runs while it isn't.
        /// </summary>
        private static void PlaceInstalledCopy(string source)
        {
            if (File.Exists(InstalledExePath) && IsExecutableRunning(InstalledExePath)) {
                _log.Info($"{InstalledExePath} is running - it will be refreshed the next time it isn't.");
                return;
            }

            // Copy next to it and rename over it, so a failed copy never leaves a truncated binary behind.
            var tempPath = InstalledExePath + ".tmp";
            try {
                File.Copy(source, tempPath, overwrite: true);
                MakeExecutable(tempPath);
                File.Move(tempPath, InstalledExePath, overwrite: true);
            }
            catch {
                if (File.Exists(tempPath)) {
                    File.Delete(tempPath);
                }
                throw;
            }
            _log.Info($"Installed binary to {InstalledExePath}");
        }

        // Linux won't open an executable for writing while any process is running it (ETXTBSY). Opening
        // without truncating or writing anything leaves the file untouched either way.
        private static bool IsExecutableRunning(string path)
        {
            try {
                using (File.OpenHandle(path, FileMode.Open, FileAccess.Write)) { }
                return false;
            }
            catch (IOException) {
                return true;
            }
        }

        private static bool PathsEqual(string a, string b) => Path.GetFullPath(a) == Path.GetFullPath(b);

        private static bool IsRunningNewerThanInstalled(string running)
        {
            var runningVersion = ReleaseVersion.Current();
            var installedVersion = GetVersionOf(InstalledExePath);
            // Couldn't determine the installed copy's version (corrupt, predates --version's current
            // format, etc.) - treat it as older, i.e. go ahead and refresh it.
            return installedVersion == null || runningVersion > installedVersion;
        }

        /// <summary>Runs `&lt;exePath&gt; --version` and parses a version out of its output, or null on
        /// any failure (bad binary, timeout, ...) - a short-lived, best-effort check, not a hard dependency.</summary>
        private static ReleaseVersion GetVersionOf(string exePath)
        {
            try {
                var psi = new ProcessStartInfo(exePath, "--version") {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                using var process = Process.Start(psi);
                if (process == null) {
                    return null;
                }

                // Drain stdout concurrently with waiting - --version's output is tiny, but this avoids
                // the classic deadlock if it weren't (child blocks writing to a full pipe nobody's reading).
                var outputTask = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(3000) || !outputTask.Wait(1000)) {
                    try { process.Kill(entireProcessTree: true); } catch (Exception) { /* best effort */ }
                    return null;
                }
                return ReleaseVersion.Parse(outputTask.Result);
            }
            catch (Exception ex) {
                _log.Debug(ex, $"Could not determine the version of the installed copy at {exePath}");
                return null;
            }
        }

        private static void MakeExecutable(string path)
        {
            // .NET has no chmod wrapper; File.Copy preserves the source's permission bits, which are
            // already +x for a published apphost, so this only matters if that ever stops being true.
            try {
                var mode = File.GetUnixFileMode(path);
                File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch (Exception ex) {
                _log.Debug($"Could not set the executable bit on {path}: {ex.Message}");
            }
        }
    }
}
