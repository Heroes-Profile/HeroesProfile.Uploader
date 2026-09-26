using System;

namespace Heroesprofile.Uploader.Desktop.Platform
{
    /// <summary>
    /// Everything the app does differently per OS. The rest of the app (and Common) is shared; code
    /// that needs an OS-specific answer asks <see cref="Platforms.Current"/> instead of checking the OS
    /// itself, so adding or changing an OS happens in one class.
    /// </summary>
    public interface IPlatform
    {
        /// <summary>"Windows", "Linux" or "macOS" - for logs, --version and user-facing text.</summary>
        string Name { get; }

        /// <summary>Folder holding config.json.</summary>
        string ConfigDir { get; }

        /// <summary>Folder holding the upload history (replays_v8.xml) and logs.</summary>
        string DataDir { get; }

        /// <summary>
        /// The Heroes of the Storm "Accounts" folder to use when none is configured, or null when there's
        /// no fixed location (Linux: the game runs in a Wine/Proton prefix the user has to point us at).
        /// </summary>
        string DefaultReplayFolder { get; }

        /// <summary>
        /// Where the game writes its .battlelobby file (read by the pre-match page and Twitch extension)
        /// when that isn't the OS temp folder LiveMonitor watches by default, or null to keep the default.
        /// On Linux it comes from the Wine prefix instead - see <see cref="ReplayFolderSetup"/>.
        /// </summary>
        string DefaultBattleLobbyFolder { get; }

        /// <summary>
        /// True when the configured replay path is a Wine/Proton prefix to search for the Accounts and
        /// temp folders (Linux), rather than the Accounts folder itself.
        /// </summary>
        bool ReplayPathIsWinePrefix { get; }

        /// <summary>
        /// True when a new replay has to be watched until it stops growing before it's read. Windows'
        /// file locking makes the game's half-written replay unreadable anyway; Linux and macOS have no
        /// such locking, so the file would otherwise be parsed while it's still being written.
        /// </summary>
        bool UseSettledMonitor { get; }

        /// <summary>Whether "Start on login" is available on this OS yet.</summary>
        bool SupportsStartOnLogin { get; }

        /// <summary>Registers or removes this app from starting (minimized) when the user logs in.</summary>
        void SetStartOnLogin(bool enabled);

        /// <summary>Unix domain socket used to keep the GUI to one instance per user.</summary>
        string SingleInstanceSocketPath { get; }

        /// <summary>
        /// Turns a secret (the Twitch uploader key) into what's stored in config.json. Windows encrypts
        /// it for the current user with DPAPI, as the WPF app did; elsewhere the config file itself is
        /// private to the user (0600) and the value is stored as-is.
        /// </summary>
        string ProtectSecret(string secret);

        /// <summary>Reverses <see cref="ProtectSecret"/>. A value that can't be decrypted (another user or machine) comes back empty.</summary>
        string UnprotectSecret(string stored);
    }

    public static class Platforms
    {
        /// <summary>
        /// Set to a folder to keep config.json, the upload history, logs and the single-instance socket
        /// there instead of the OS's usual places - a portable install, or a test run that mustn't touch
        /// the real ones.
        /// </summary>
        public const string HomeOverrideVariable = "HEROESPROFILE_UPLOADER_HOME";

        /// <summary>The platform this process is running on.</summary>
        public static IPlatform Current { get; } = Detect(HomeOverride());

        /// <summary>The <see cref="HomeOverrideVariable"/> folder, or null to use the OS's usual places.</summary>
        private static string HomeOverride()
        {
            var home = Environment.GetEnvironmentVariable(HomeOverrideVariable);
            return string.IsNullOrWhiteSpace(home) ? null : System.IO.Path.GetFullPath(home);
        }

        /// <param name="home">A folder for config, data and the socket instead of the OS's usual places, or null.</param>
        internal static IPlatform Detect(string home)
        {
            if (OperatingSystem.IsWindows()) {
                return new WindowsPlatform(home);
            }
            if (OperatingSystem.IsMacOS()) {
                return new MacPlatform(home);
            }
            if (OperatingSystem.IsLinux()) {
                return new LinuxPlatform(home);
            }
            throw new PlatformNotSupportedException("Heroes Profile Uploader runs on Windows, Linux and macOS.");
        }
    }
}
