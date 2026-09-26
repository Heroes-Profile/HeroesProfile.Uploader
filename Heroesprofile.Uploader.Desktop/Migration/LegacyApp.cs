using Microsoft.Win32;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;

namespace Heroesprofile.Uploader.Desktop.Migration
{
    /// <summary>
    /// The WPF uploader this app replaces. Running both at once has each upload every game (the second
    /// copy is rejected as a duplicate) and open every match page, hence the "still running" warning.
    ///
    /// It can also be removed from here (the first-run "Remove the old uploader?" prompt), using its own
    /// uninstaller. That deletes its whole %APPDATA%\Heroesprofile folder, which is fine by then: this
    /// app keeps its own copies of the settings and upload history (WpfMigration), and makes sure of the
    /// history once more right before uninstalling.
    /// </summary>
    internal static class LegacyApp
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        // The WPF app's executable name (its AssemblyName).
        private const string ProcessName = "Heroesprofile.Uploader";

        // Its Squirrel install, and the name of its entry under ...\CurrentVersion\Uninstall.
        private const string InstallFolderName = "Heroesprofile";
        private const string UninstallKeyName = "Heroesprofile";
        private const string UninstallKeysPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        // What its shortcuts were called (desktop, Start menu, Startup) - see RemoveShortcuts.
        private const string ShortcutName = "Heroesprofile Uploader.lnk";
        private const string StartMenuFolderName = "Heroes Profile";

        public const string RunningWarning =
            "The old Heroes Profile uploader is still running. Running both at once uploads every game twice " +
            "and opens every match page twice, so close the old one first (right-click its tray icon, or quit " +
            "it from the Task Manager).";

        public const string RemovePrompt =
            "The old Heroes Profile uploader is still installed. This app replaces it, and has already " +
            "copied over your settings and upload history.\n\n" +
            "Uninstall the old uploader now? It also stops it starting with Windows. Your settings and " +
            "upload history in this app are kept.";

        /// <summary>True when the WPF app is running for any user on this machine. Always false off Windows.</summary>
        public static bool IsRunning()
        {
            if (!OperatingSystem.IsWindows()) {
                return false;
            }
            var processes = Process.GetProcessesByName(ProcessName);
            try {
                return processes.Any();
            }
            finally {
                foreach (var p in processes) {
                    p.Dispose();
                }
            }
        }

        /// <summary>%LOCALAPPDATA%\Heroesprofile, where Squirrel installed the WPF app.</summary>
        private static string InstallRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InstallFolderName);

        /// <summary>True when the WPF app is installed for this user. Always false off Windows.</summary>
        public static bool IsInstalled() => OperatingSystem.IsWindows() && IsInstalledIn(InstallRoot);

        internal static bool IsInstalledIn(string root) =>
            File.Exists(Path.Combine(root, "Update.exe")) &&
            (File.Exists(Path.Combine(root, "Heroesprofile.Uploader.exe")) || Directory.EnumerateDirectories(root, "app-*").Any());

        /// <summary>
        /// Uninstalls the WPF app with its own uninstaller, after making sure this app has its own copy of
        /// the upload history. Then removes whatever the uninstaller left behind - notably the Startup
        /// shortcut the WPF app created itself - so nothing of it starts at login. Throws if its program
        /// files can't be removed (e.g. still in use).
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static void Remove()
        {
            // The uninstaller deletes %APPDATA%\Heroesprofile, history included - never before we have ours.
            WpfMigration.CopyHistory(WpfMigration.OldDataDir, AppConfig.DataDir);

            StopRunning();

            var root = InstallRoot;
            var uninstaller = Path.Combine(root, "Update.exe");
            if (File.Exists(uninstaller)) {
                try {
                    using var run = Process.Start(new ProcessStartInfo(uninstaller, "--uninstall") { UseShellExecute = false });
                    if (run != null && !run.WaitForExit(120_000)) {
                        _log.Warn("The old uploader's uninstaller is taking a long time - cleaning up what's left anyway");
                    }
                }
                catch (Exception ex) {
                    _log.Warn(ex, "The old uploader's uninstaller failed - removing its files directly");
                }
            }

            // Whatever the uninstaller didn't (or couldn't) remove.
            if (Directory.Exists(root)) {
                RemoveProgramFiles(root);
            }
            RemoveShortcuts(root, new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            }, Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            RemoveUninstallEntry(root);
            _log.Info("Uninstalled the old WPF uploader (this app keeps its own settings and upload history)");
        }

        private static void StopRunning()
        {
            foreach (var process in Process.GetProcessesByName(ProcessName)) {
                using (process) {
                    try {
                        process.Kill();
                        process.WaitForExit(10_000);
                    }
                    catch (Exception ex) {
                        _log.Warn(ex, "Could not stop the old uploader");
                    }
                }
            }
        }

        /// <summary>
        /// Deletes the Squirrel install's own files from <paramref name="root"/> - Update.exe, the launcher,
        /// the app-x.y.z folders, packages and Squirrel's logs - if its uninstaller left any. Nothing else:
        /// the folder also holds .NET's user.config folders.
        /// </summary>
        internal static void RemoveProgramFiles(string root)
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "app-*").Concat(new[] { Path.Combine(root, "packages") })) {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, recursive: true);
                }
            }
            foreach (var file in new[] { "Update.exe", "Heroesprofile.Uploader.exe", "SquirrelSetup.log", ".dead" }
                .Select(f => Path.Combine(root, f))
                .Concat(Directory.EnumerateFiles(root, "Squirrel-*.log"))) {
                if (File.Exists(file)) {
                    File.Delete(file);
                }
            }
        }

        /// <summary>
        /// Deletes the WPF app's shortcuts - but only ones that actually point into its install folder,
        /// never by name alone, so this app's own "Heroes Profile Uploader" shortcuts are safe. Also removes
        /// its Start menu folder once that's empty.
        /// </summary>
        internal static void RemoveShortcuts(string root, IEnumerable<string> folders, string programsFolder)
        {
            var startMenuFolder = Path.Combine(programsFolder, StartMenuFolderName);
            foreach (var folder in folders.Append(startMenuFolder)) {
                var shortcut = Path.Combine(folder, ShortcutName);
                if (File.Exists(shortcut) && PointsInto(shortcut, root)) {
                    File.Delete(shortcut);
                    _log.Info($"Removed {shortcut}");
                }
            }
            if (Directory.Exists(startMenuFolder) && !Directory.EnumerateFileSystemEntries(startMenuFolder).Any()) {
                Directory.Delete(startMenuFolder);
            }
        }

        /// <summary>
        /// True when the .lnk file mentions a path inside <paramref name="root"/>. A shortcut stores its
        /// target path as text (ANSI and/or UTF-16), so looking for it in the bytes is enough here without
        /// pulling in the Shell COM API.
        /// </summary>
        internal static bool PointsInto(string shortcut, string root)
        {
            var bytes = File.ReadAllBytes(shortcut);
            var marker = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            return Contains(bytes, Encoding.Unicode.GetBytes(marker)) ||
                   Contains(bytes, Encoding.Unicode.GetBytes(marker.ToLowerInvariant())) ||
                   Contains(bytes, Encoding.Latin1.GetBytes(marker)) ||
                   Contains(bytes, Encoding.Latin1.GetBytes(marker.ToLowerInvariant()));
        }

        private static bool Contains(byte[] haystack, byte[] needle) =>
            haystack.AsSpan().IndexOf(needle) >= 0;

        /// <summary>Removes the WPF app's entry from Windows' Apps list, if it's the one for this install.</summary>
        [SupportedOSPlatform("windows")]
        private static void RemoveUninstallEntry(string root)
        {
            using var uninstall = Registry.CurrentUser.OpenSubKey(UninstallKeysPath, writable: true);
            using var entry = uninstall?.OpenSubKey(UninstallKeyName);
            var uninstallString = entry?.GetValue("UninstallString") as string ?? "";
            // Only the old app's: its uninstaller is Update.exe in its own install folder. (This app's
            // entry is "Heroesprofile.Uploader", pointing at its own folder.)
            if (uninstallString.IndexOf(Path.Combine(root, "Update.exe"), StringComparison.OrdinalIgnoreCase) >= 0) {
                entry.Dispose();
                uninstall.DeleteSubKeyTree(UninstallKeyName, throwOnMissingSubKey: false);
                _log.Info($"Removed the old uploader's Apps entry ({UninstallKeyName})");
            }
        }
    }
}
