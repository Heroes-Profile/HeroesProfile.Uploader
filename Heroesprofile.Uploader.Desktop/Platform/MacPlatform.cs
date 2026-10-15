using NLog;
using System;
using System.IO;
using System.Runtime.Versioning;

namespace Heroesprofile.Uploader.Desktop.Platform
{
    [SupportedOSPlatform("macos")]
    internal sealed class MacPlatform : IPlatform
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private const string LaunchAgentLabel = "com.heroesprofile.uploader";
        private const string KeychainService = "Heroes Profile Uploader";

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // ~/Library/Application Support. Not SpecialFolder.ApplicationData, which .NET maps to ~/.config on macOS.
        private static string ApplicationSupport => Path.Combine(Home, "Library", "Application Support");

        private readonly string _home;

        /// <param name="home">Folder for config, data and the socket instead of the usual places, or null (see Platforms.HomeOverrideVariable).</param>
        public MacPlatform(string home = null)
        {
            _home = home;
        }

        public string Name => "macOS";

        public string ConfigDir => DataDir;
        public string DataDir => _home ?? Path.Combine(ApplicationSupport, "Heroesprofile");

        // Where the native Mac client keeps replays and storm saves (same layout as Windows' Documents).
        public string DefaultReplayFolder => Path.Combine(ApplicationSupport, "Blizzard", "Heroes of the Storm", "Accounts");

        /// <summary>
        /// The user's whole /var/folders/&lt;xx&gt;/&lt;hash&gt;/ area - the parent of $TMPDIR (its T/ folder).
        /// The Mac client writes replay.server.battlelobby somewhere under /private/var/folders (the old
        /// Heroes Share Live client searched there); watching the parent covers T/ and C/ (caches) alike
        /// until a real game confirms which one.
        /// </summary>
        public string DefaultBattleLobbyFolder => Path.GetDirectoryName(Path.GetTempPath().TrimEnd('/'));

        public bool ReplayPathIsWinePrefix => false;
        public bool UseSettledMonitor => true;

        public bool SupportsStartOnLogin => true;

        private static string LaunchAgentPath => Path.Combine(Home, "Library", "LaunchAgents", $"{LaunchAgentLabel}.plist");

        /// <summary>
        /// A per-user LaunchAgent that starts the app minimized at login. launchd picks it up at the next
        /// login, so nothing has to be loaded now. Inside an .app bundle it goes through `open -a` so
        /// macOS starts it as a proper app (Dock, menu bar) rather than a bare executable.
        /// </summary>
        public void SetStartOnLogin(bool enabled)
        {
            if (!enabled) {
                if (File.Exists(LaunchAgentPath)) {
                    File.Delete(LaunchAgentPath);
                    _log.Info($"Removed {LaunchAgentPath}");
                }
                return;
            }

            var exe = Environment.ProcessPath;
            var bundle = MacLaunchAgent.AppBundleOf(exe);
            var arguments = bundle != null
                ? new[] { "/usr/bin/open", "-a", bundle, "--args", "--minimized" }
                : new[] { exe, "--minimized" };

            Directory.CreateDirectory(Path.GetDirectoryName(LaunchAgentPath));
            File.WriteAllText(LaunchAgentPath, MacLaunchAgent.Plist(LaunchAgentLabel, arguments));
            _log.Info($"Wrote {LaunchAgentPath}");
        }

        public string SingleInstanceSocketPath => Path.Combine(DataDir, "instance.sock");

        // Marks a value kept in the Keychain; anything else in config.json is a plain value (a Linux
        // config copied over, or the fallback below).
        private const string KeychainMarker = "keychain:";

        // One Keychain item per config folder, so a portable or test home never touches the real one.
        private string KeychainAccount => $"TwitchUploaderKey ({ConfigDir})";

        /// <summary>
        /// Puts the secret in the login Keychain and leaves only a marker in config.json. If the
        /// Keychain can't be used, keeps it in the 0600 config file as on Linux rather than losing it.
        /// </summary>
        public string ProtectSecret(string secret)
        {
            try {
                if (string.IsNullOrEmpty(secret)) {
                    MacKeychain.Delete(KeychainService, KeychainAccount);
                    return "";
                }
                MacKeychain.Set(KeychainService, KeychainAccount, secret);
                return KeychainMarker;
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not use the Keychain - keeping the Twitch key in config.json (readable only by you)");
                return secret ?? "";
            }
        }

        public string UnprotectSecret(string stored)
        {
            if (stored != KeychainMarker) {
                return stored ?? "";
            }
            try {
                return MacKeychain.Get(KeychainService, KeychainAccount) ?? "";
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not read the Twitch key from the Keychain; it needs entering again");
                return "";
            }
        }
    }
}
