using Heroesprofile.Uploader.Common;
using NLog;
using System;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// Resolves the configured prefix into the Accounts and temp folders Common needs, and points
    /// ReplayLocation/LiveMonitor at them. Shared by the `run` and `scan` commands.
    /// </summary>
    internal static class PrefixSetup
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        /// <summary>--prefix wins over the config file; neither set is a setup error, not a crash.</summary>
        public static string ResolvePrefix(string cliOverride, AppConfig config)
        {
            var prefix = !string.IsNullOrWhiteSpace(cliOverride) ? cliOverride.Trim() : config.Prefix;
            if (string.IsNullOrWhiteSpace(prefix)) {
                throw new ConfigError(
                    $"No prefix configured. Pass --prefix <path>, or set \"prefix\" in {AppConfig.ConfigPath}.");
            }
            return prefix;
        }

        /// <summary>
        /// Resolves the Accounts and temp folders from <paramref name="prefix"/> and points
        /// <see cref="ReplayLocation"/>/<see cref="LiveMonitor"/> at them.
        /// </summary>
        public static void Apply(string prefix)
        {
            var accounts = WinePrefix.FindAccounts(prefix);
            if (accounts == null) {
                throw new ConfigError(
                    $"Could not find a Heroes of the Storm \"Accounts\" folder under prefix '{prefix}'. " +
                    "Expected drive_c/users/*/Documents/Heroes of the Storm/Accounts, optionally under a pfx/ " +
                    "subfolder (Steam compatdata/<appid> layout) - or point --prefix straight at the Accounts folder.");
            }

            var temp = WinePrefix.FindTemp(prefix);
            if (temp == null) {
                _log.Warn($"Could not find the prefix's temp folder (drive_c/users/*/AppData/Local/Temp) under '{prefix}'. " +
                    "The pre-match page and Twitch extension won't see the battle lobby, but replay uploads are unaffected.");
            }

            ReplayLocation.CustomPath = accounts;
            LiveMonitor.BattleLobbyTempPathOverride = temp;
        }

        /// <summary>
        /// GUI-friendly version of <see cref="Apply"/>: resolves and applies a prefix without throwing,
        /// for the first-run "point me at your prefix" flow and the settings dialog, where a bad path
        /// is a status message, not a fatal error.
        /// </summary>
        /// <param name="prefix">Prefix root, compatdata folder, or Accounts folder</param>
        /// <param name="accounts">The resolved Accounts folder, if found</param>
        /// <returns>true if a Heroes of the Storm Accounts folder was found and applied</returns>
        public static bool TryApply(string prefix, out string accounts)
        {
            accounts = WinePrefix.FindAccounts(prefix);
            if (accounts == null) {
                return false;
            }

            LiveMonitor.BattleLobbyTempPathOverride = WinePrefix.FindTemp(prefix);
            ReplayLocation.CustomPath = accounts;
            return true;
        }
    }
}
