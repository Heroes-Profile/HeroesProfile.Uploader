using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// For testers of the macOS and Linux Ranks builds: prerelease builds keep a copy of what each game's capture
    /// caught - every frame's card strips, and what was read off them - in a folder next to the app, so testers
    /// can see whether it's catching the right window. Every mode is kept (only Storm League is ever sent), on
    /// this machine only, newest <see cref="GamesKept"/> games. Full releases, and Windows, keep nothing.
    /// HP_RANK_CAPTURES=1 turns it on in any build.
    /// </summary>
    internal static class CaptureDump
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public const string FolderName = "HeroesProfileRankCaptures";
        public const int GamesKept = 20;

        private static readonly Lazy<string> _folder = new Lazy<string>(ChooseFolder);

        /// <summary>Whether this build keeps captures: a prerelease on macOS or Linux, or HP_RANK_CAPTURES=1.</summary>
        public static bool Enabled =>
            Environment.GetEnvironmentVariable("HP_RANK_CAPTURES") == "1"
            || ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && IsPrerelease(AppVersion));

        /// <summary>Where the games' folders go (chosen once).</summary>
        public static string Folder => _folder.Value;

        private static string AppVersion =>
            Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

        /// <summary>Whether a version (as the app reports it, "3.0.2-beta.1+abc123") is a prerelease.</summary>
        public static bool IsPrerelease(string version) => version.Split('+')[0].Contains('-');

        /// <summary>
        /// Saves one game's frames and reading into a new folder; drops the oldest games past <see cref="GamesKept"/>.
        /// Never throws - a tester's copy isn't worth failing the game over.
        /// </summary>
        public static void Save(string mode, IReadOnlyList<CapturedFrame> frames, RankReader.GameReading reading, string root = null)
        {
            try {
                root ??= Folder;
                var game = Path.Combine(root, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{mode}");
                Directory.CreateDirectory(game);
                for (var i = 0; i < frames.Count; i++) {
                    File.WriteAllBytes(Path.Combine(game, $"frame-{i:00}-left.png"), frames[i].LeftPng);
                    File.WriteAllBytes(Path.Combine(game, $"frame-{i:00}-right.png"), frames[i].RightPng);
                }
                File.WriteAllText(Path.Combine(game, "read.txt"), Describe(mode, frames, reading));
                Prune(root);
                _log.Info($"Rank reading: kept a copy of this game's capture in {game}");
            }
            catch (Exception ex) {
                _log.Warn(ex, "Rank reading: couldn't keep a copy of the capture");
            }
        }

        private static string Describe(string mode, IReadOnlyList<CapturedFrame> frames, RankReader.GameReading reading)
        {
            var text = new StringBuilder();
            text.AppendLine($"Game mode: {mode} ({(mode == "StormLeague" ? "ranks sent once the replay uploads" : "nothing sent")})");
            text.AppendLine($"Text readers: {string.Join(", ", CardText.Languages.DefaultIfEmpty("none"))}");
            text.AppendLine();
            for (var i = 0; i < frames.Count; i++) {
                var loading = reading?.LoadingScreenFrames.Contains(i) == true ? "loading screen" : "not the loading screen";
                text.AppendLine($"frame-{i:00}: {frames[i].Width} x {frames[i].Height}, {frames[i].At:HH:mm:ss}, {loading}");
            }
            text.AppendLine();
            if (reading?.Cards == null) {
                text.AppendLine("No frame showed the loading screen, so nothing was read.");
            } else {
                foreach (var card in reading.Cards) {
                    text.AppendLine($"{card.Rank}   name: {string.Join(" | ", card.Text.Names.DefaultIfEmpty("(nothing read)"))}   hero: {string.Join(" | ", card.Text.Heroes.DefaultIfEmpty("(nothing read)"))}");
                }
            }
            return text.ToString();
        }

        private static void Prune(string root)
        {
            foreach (var old in Directory.GetDirectories(root).OrderByDescending(d => d, StringComparer.Ordinal).Skip(GamesKept)) {
                Directory.Delete(old, recursive: true);
            }
        }

        /// <summary>
        /// Next to the installed app: beside the AppImage on Linux (its own folder is a read-only mount), beside
        /// the .app on macOS (writing inside it would break its signature), else the app's own folder. Somewhere
        /// writable in the temp folder if that one isn't.
        /// </summary>
        private static string ChooseFolder()
        {
            var appDir = AppContext.BaseDirectory;
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is string appImage && appImage.Length > 0) {
                appDir = Path.GetDirectoryName(appImage) ?? appDir;
            } else if (OperatingSystem.IsMacOS()) {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) {
                    dir = dir.Parent;
                }
                appDir = dir?.Parent?.FullName ?? appDir;
            }

            var folder = Path.Combine(appDir, FolderName);
            if (IsWritable(folder)) {
                return folder;
            }
            var fallback = Path.Combine(Path.GetTempPath(), FolderName);
            _log.Info($"Rank reading: can't write next to the app ({folder}), keeping captures in {fallback} instead");
            return fallback;
        }

        private static bool IsWritable(string folder)
        {
            try {
                Directory.CreateDirectory(folder);
                var probe = Path.Combine(folder, ".write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception) {
                return false;
            }
        }
    }
}
