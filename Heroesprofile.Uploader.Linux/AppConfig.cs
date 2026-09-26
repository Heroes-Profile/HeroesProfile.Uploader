using Newtonsoft.Json;
using System;
using System.IO;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// User settings, loaded from $XDG_CONFIG_HOME/heroesprofile/config.json (falls back to
    /// ~/.config/heroesprofile/config.json). Only "prefix" is required - the pre/post match pages
    /// default off, since a headless systemd setup has no desktop for xdg-open to hand a URL to.
    /// GUI-only settings (theme, tray/login behaviour, log level, Twitch) live here too, so the CLI
    /// (`run`/`scan`) and the GUI read and write the same file.
    /// </summary>
    public class AppConfig
    {
        public string Prefix { get; set; }
        public bool PreMatchPage { get; set; }
        public bool PostMatchPage { get; set; }
        public string WebhookUrl { get; set; }

        /// <summary>"Dark", "Light", or "System" (follow the desktop theme). Defaults to "System".</summary>
        public string Theme { get; set; } = "System";
        public bool TwitchExtension { get; set; }

        /// <summary>
        /// Twitch uploader key, stored in plain text. There's no Linux equivalent of the Windows
        /// build's DPAPI-encrypted setting, and config.json is already a private, 0600-ish file under
        /// $XDG_CONFIG_HOME - same trust boundary as an SSH key or .netrc.
        /// </summary>
        public string TwitchUploaderKey { get; set; }

        public bool MinimizeToTray { get; set; }
        public bool StartOnLogin { get; set; }

        /// <summary>NLog level name: Trace/Debug/Info/Warn/Error/Fatal. Defaults to "Info".</summary>
        public string LogLevel { get; set; } = "Info";

        /// <summary>
        /// GitHub "owner/repo" that "Check for update" looks at - same idea as the Windows app's
        /// UpdateRepository setting. Point it at a fork to follow test builds.
        /// </summary>
        public string UpdateRepository { get; set; } = "Heroes-Profile/HeroesProfile.Uploader";

        /// <summary>Check for updates on startup/hourly (GUI) or startup/every 24h (`run`) and stage them automatically.</summary>
        public bool AutoUpdate { get; set; } = true;

        /// <summary>Also consider prerelease GitHub releases (test builds) when checking for updates.</summary>
        public bool AllowPreReleases { get; set; }

        private static string XdgHome(string envVar, string fallbackLeaf)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallbackLeaf);
        }

        /// <summary>$XDG_CONFIG_HOME, or ~/.config</summary>
        private static string ConfigHome => XdgHome("XDG_CONFIG_HOME", ".config");

        /// <summary>$XDG_DATA_HOME, or ~/.local/share</summary>
        private static string DataHome => XdgHome("XDG_DATA_HOME", Path.Combine(".local", "share"));

        public static string ConfigPath => Path.Combine(ConfigHome, "heroesprofile", "config.json");
        public static string DataDir => Path.Combine(DataHome, "heroesprofile");

        /// <summary>
        /// Loads the config file, or an empty (all-default) config if it doesn't exist yet - "no prefix
        /// configured" is reported by the caller once it knows whether --prefix covered for it.
        /// </summary>
        public static AppConfig Load()
        {
            if (!File.Exists(ConfigPath)) {
                return new AppConfig();
            }

            try {
                return JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
            }
            catch (Exception ex) {
                throw new ConfigError($"Could not read config file {ConfigPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes this config back to <see cref="ConfigPath"/>, creating its folder if needed. Contains
        /// the Twitch uploader key in plain text (see <see cref="TwitchUploaderKey"/>), so the file is
        /// created 0600 (no world/group read) with no window where it's briefly world-readable, and any
        /// pre-existing file (created 0644 before this existed) is chmod'd back down to 0600 too.
        /// </summary>
        public void Save()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            // UnixCreateMode/SetUnixFileMode are Linux/Unix-only APIs (CA1416) - fine, this whole
            // project only ever runs on Linux.
#pragma warning disable CA1416
            using (var stream = new FileStream(ConfigPath, new FileStreamOptions {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            }))
            using (var writer = new StreamWriter(stream)) {
                writer.Write(json);
            }

            // UnixCreateMode only takes effect when the file is actually created - a file that already
            // existed (e.g. 0644 from before this code existed) keeps its old mode across FileMode.Create.
            File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#pragma warning restore CA1416
        }
    }
}
