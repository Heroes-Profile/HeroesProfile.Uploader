using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop.Platform;
using NLog;
using System.IO;

namespace Heroesprofile.Uploader.Desktop
{
    /// <summary>
    /// Turns the configured replay path into the Accounts and battle lobby folders Common needs, and
    /// points ReplayLocation/LiveMonitor at them. What the path means depends on the platform: on Linux
    /// it's a Wine/Proton prefix to search (or the Accounts folder itself); on Windows and macOS it's the
    /// Accounts folder, and empty means the game's default location. Shared by the GUI, `run` and `scan`.
    /// </summary>
    internal static class ReplayFolderSetup
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public sealed class Folders
        {
            public Folders(string accounts, string battleLobby)
            {
                Accounts = accounts;
                BattleLobby = battleLobby;
            }

            /// <summary>The Heroes of the Storm "Accounts" folder holding replays and storm saves.</summary>
            public string Accounts { get; }

            /// <summary>Where the game writes .battlelobby files, or null for LiveMonitor's default (the OS temp folder).</summary>
            public string BattleLobby { get; }
        }

        /// <summary>
        /// Resolves <paramref name="configured"/> (may be empty) on <paramref name="platform"/>, or
        /// returns null with a user-facing <paramref name="error"/> when no replay folder can be found.
        /// </summary>
        public static Folders Resolve(IPlatform platform, string configured, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(configured)) {
                var defaultFolder = platform.DefaultReplayFolder;
                if (defaultFolder == null) {
                    error = "No Wine/Proton prefix is set yet.";
                    return null;
                }
                if (!Directory.Exists(defaultFolder)) {
                    error = $"Couldn't find your replay folder at {defaultFolder}.";
                    return null;
                }
                return new Folders(defaultFolder, platform.DefaultBattleLobbyFolder);
            }

            var path = configured.Trim();
            if (platform.ReplayPathIsWinePrefix) {
                var accounts = WinePrefix.FindAccounts(path);
                if (accounts == null) {
                    error = $"Couldn't find a Heroes of the Storm \"Accounts\" folder in the prefix {path}.";
                    return null;
                }
                return new Folders(accounts, WinePrefix.FindTemp(path));
            }

            if (!Directory.Exists(path)) {
                error = $"The replay folder {path} doesn't exist.";
                return null;
            }
            return new Folders(path, platform.DefaultBattleLobbyFolder);
        }

        /// <summary>Points ReplayLocation (replays, storm saves) and LiveMonitor (battle lobby) at <paramref name="folders"/>.</summary>
        public static void Apply(Folders folders)
        {
            LiveMonitor.BattleLobbyTempPathOverride = folders.BattleLobby;
            ReplayLocation.CustomPath = folders.Accounts;
        }

        /// <summary>
        /// GUI version: resolves and applies without throwing, for the first-run screen and the
        /// settings dialog, where a bad path is a status message rather than a fatal error.
        /// </summary>
        /// <returns>true if a replay folder was found and applied</returns>
        public static bool TryApply(IPlatform platform, string configured, out string error)
        {
            var folders = Resolve(platform, configured, out error);
            if (folders == null) {
                return false;
            }
            WarnIfNoBattleLobby(platform, folders);
            Apply(folders);
            return true;
        }

        /// <summary>
        /// CLI version (`run`/`scan`): --prefix wins over the config file. A replay folder that can't be
        /// found is a <see cref="ConfigError"/> - a setup problem to report, not a crash.
        /// </summary>
        public static Folders ApplyForCommand(IPlatform platform, string cliOverride, AppConfig config)
        {
            var configured = !string.IsNullOrWhiteSpace(cliOverride) ? cliOverride : config.ReplayPath;
            var folders = Resolve(platform, configured, out var error);
            if (folders == null) {
                var hint = platform.ReplayPathIsWinePrefix
                    ? "Pass --prefix <path> (a Wine/Proton prefix, a Steam compatdata/<appid> folder, or the " +
                      "Heroes of the Storm \"Accounts\" folder), or set \"ReplayPath\" in " + AppConfig.ConfigPath + "."
                    : "Pass --prefix <path> with your Heroes of the Storm \"Accounts\" folder, or set " +
                      "\"ReplayPath\" in " + AppConfig.ConfigPath + ".";
                throw new ConfigError($"{error} {hint}");
            }

            WarnIfNoBattleLobby(platform, folders);
            Apply(folders);
            return folders;
        }

        private static void WarnIfNoBattleLobby(IPlatform platform, Folders folders)
        {
            if (platform.ReplayPathIsWinePrefix && folders.BattleLobby == null) {
                _log.Warn("Could not find the prefix's temp folder (drive_c/users/*/AppData/Local/Temp). " +
                    "The pre-match page and Twitch extension won't see the battle lobby, but replay uploads are unaffected.");
            }
        }
    }
}
