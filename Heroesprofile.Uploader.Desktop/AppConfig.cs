using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop.Migration;
using Heroesprofile.Uploader.Desktop.Platform;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NLog;
using System;
using System.IO;

namespace Heroesprofile.Uploader.Desktop
{
    /// <summary>
    /// User settings, in config.json under the platform's config folder (see <see cref="IPlatform.ConfigDir"/>).
    /// The pre/post match pages default off, since a headless systemd setup has no desktop to hand a
    /// URL to. GUI-only settings (theme, tray/login behaviour, log level, Twitch) live here too, so the
    /// CLI (`run`/`scan`) and the GUI read and write the same file.
    /// </summary>
    public class AppConfig
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Where the replays are. Linux: a Wine/Proton prefix, a Steam compatdata/&lt;appid&gt; folder,
        /// or the Heroes of the Storm "Accounts" folder itself. Windows/macOS: the Accounts folder, with
        /// empty meaning the game's default location. See <see cref="ReplayFolderSetup"/>.
        /// </summary>
        public string ReplayPath { get; set; }

        /// <summary>
        /// Read-only alias for config.json files written by the Linux-only app, which called this
        /// setting "Prefix". Never written back; an explicit ReplayPath wins.
        /// </summary>
        [JsonProperty("Prefix")]
        private string LegacyPrefix
        {
            set {
                if (string.IsNullOrWhiteSpace(ReplayPath)) {
                    ReplayPath = value;
                }
            }
        }

        public bool PreMatchPage { get; set; }
        public bool PostMatchPage { get; set; }
        public string WebhookUrl { get; set; }

        public const string LightTheme = "Default";
        public const string DarkTheme = "MetroDark";
        public const string SystemTheme = "System";

        private string _theme = DarkTheme;

        /// <summary>
        /// <see cref="LightTheme"/>, <see cref="DarkTheme"/> or <see cref="SystemTheme"/> (follow the
        /// desktop). The first two are the WPF app's own stored values, so its setting imports as-is;
        /// dark is the default there too. "Light"/"Dark", written by the Linux-only app, still load.
        /// </summary>
        public string Theme
        {
            get => _theme;
            set => _theme = value switch {
                "Light" or LightTheme => LightTheme,
                SystemTheme => SystemTheme,
                _ => DarkTheme,
            };
        }

        public const string Theme1Design = "Theme1";
        public const string Theme2Design = "Theme2";

        private string _design = Theme1Design;

        /// <summary>
        /// Which main-window layout to show: <see cref="Theme1Design"/>, laid out after the WPF app
        /// (the default), or <see cref="Theme2Design"/>, the design the Linux app (PR #53) shipped with.
        /// Independent of <see cref="Theme"/>, which picks light or dark for either. Unknown values load
        /// as Theme 1.
        /// </summary>
        public string Design
        {
            get => _design;
            set => _design = value == Theme2Design ? Theme2Design : Theme1Design;
        }

        public bool TwitchExtension { get; set; }

        /// <summary>
        /// Twitch uploader key, in plain text in memory only. What's written to config.json is
        /// <see cref="IPlatform.ProtectSecret"/>'s version: DPAPI-encrypted for the current user on
        /// Windows (as the WPF app stored it), as-is in the 0600 config file elsewhere.
        /// </summary>
        [JsonIgnore]
        public string TwitchUploaderKey { get; set; }

        [JsonProperty(nameof(TwitchUploaderKey))]
        private string StoredTwitchUploaderKey
        {
            get => Platforms.Current.ProtectSecret(TwitchUploaderKey);
            set => TwitchUploaderKey = Platforms.Current.UnprotectSecret(value);
        }

        /// <summary>
        /// Which replays to delete once they're handled (none by default). The WPF app had this setting
        /// without anything in its UI to change it; it's imported and honoured the same way here.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public DeleteFiles DeleteAfterUpload { get; set; }

        public bool MinimizeToTray { get; set; }
        public bool StartOnLogin { get; set; }

        // Main window placement, remembered between runs. Same defaults as the WPF app, except 60 wider
        // to make room for the replay id column: the longest map's file name
        // ("... Tomb of the Spider Queen.StormReplay") still shows in full.
        public int WindowLeft { get; set; } = 400;
        public int WindowTop { get; set; } = 400;
        public double WindowWidth { get; set; } = 760;
        public double WindowHeight { get; set; } = 600;

        // Theme 2 is laid out for a narrower window, so it remembers its own size (PR #53's default);
        // the position is shared. WindowWidth/WindowHeight above are Theme 1's, as imported from WPF.
        public double Theme2WindowWidth { get; set; } = 450;
        public double Theme2WindowHeight { get; set; } = 600;

        /// <summary>The remembered window size for <paramref name="design"/>.</summary>
        public (double Width, double Height) WindowSizeFor(string design) => design == Theme2Design
            ? (Theme2WindowWidth, Theme2WindowHeight)
            : (WindowWidth, WindowHeight);

        public void RememberWindowSize(string design, double width, double height)
        {
            if (design == Theme2Design) {
                Theme2WindowWidth = width;
                Theme2WindowHeight = height;
            } else {
                WindowWidth = width;
                WindowHeight = height;
            }
        }

        /// <summary>
        /// NLog level name for the log file. The file always gets at least Debug - each replay found and
        /// its upload result, the battle lobby, the match pages - as the WPF app's did, since that's what
        /// bug reports need; "Trace" adds more. (Earlier builds saved "Info" here, which is why lower
        /// levels can't turn it down.)
        /// </summary>
        public string LogLevel { get; set; } = "Debug";

        public const string DefaultUpdateRepository = "Heroes-Profile/HeroesProfile.Uploader";

        private string _updateRepository = DefaultUpdateRepository;

        /// <summary>
        /// GitHub "owner/repo" that updates come from - same idea as the Windows app's UpdateRepository
        /// setting. Point it at a fork to follow test builds. The WPF app stored it as a full URL
        /// ("https://github.com/owner/repo"), so that form (as imported, or typed in) is accepted too and
        /// trimmed down to "owner/repo"; empty means the default.
        /// </summary>
        public string UpdateRepository
        {
            get => _updateRepository;
            set => _updateRepository = NormalizeRepository(value);
        }

        internal static string NormalizeRepository(string value)
        {
            var repo = (value ?? "").Trim();
            foreach (var prefix in new[] { "https://github.com/", "http://github.com/", "https://www.github.com/", "github.com/" }) {
                if (repo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                    repo = repo.Substring(prefix.Length);
                    break;
                }
            }
            repo = repo.Trim('/');
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
                repo = repo.Substring(0, repo.Length - ".git".Length);
            }
            return repo.Split('/').Length == 2 && !repo.Contains(' ') ? repo : DefaultUpdateRepository;
        }

        /// <summary>Check for updates on startup/hourly (GUI) or startup/every 24h (`run`) and stage them automatically.</summary>
        public bool AutoUpdate { get; set; } = true;

        /// <summary>Also consider prerelease GitHub releases (test builds) when checking for updates.</summary>
        public bool AllowPreReleases { get; set; }

        /// <summary>
        /// "Read ranks from the loading screen" - opt-in, and only offered by the Ranks build (see
        /// RankReading); the normal build keeps whatever is here and ignores it.
        /// </summary>
        public bool ReadRanks { get; set; }

        /// <summary>
        /// Ranks build: show Windows' yellow outline around the game while rank reading captures it. Off by
        /// default; Windows 10 always shows it regardless.
        /// </summary>
        public bool ShowCaptureOutline { get; set; }

        public static string ConfigPath => Path.Combine(Platforms.Current.ConfigDir, "config.json");
        public static string DataDir => Platforms.Current.DataDir;

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
                return FromJson(File.ReadAllText(ConfigPath));
            }
            catch (Exception ex) {
                throw new ConfigError($"Could not read config file {ConfigPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// <see cref="Load"/>, except that on the very first run on Windows - no config.json yet - the
        /// WPF app's settings are imported and (with <paramref name="save"/>) saved as the starting config,
        /// and its upload history is copied over, so nothing is uploaded twice (see WpfMigration).
        /// </summary>
        public static AppConfig LoadOrImport(bool save = true)
        {
            if (!File.Exists(ConfigPath) && OperatingSystem.IsWindows() &&
                WpfMigration.TryImportSettings(out var imported, out var source)) {
                _log.Info($"No config.json yet - imported the Windows app's settings from {source}");
                try {
                    if (save) {
                        imported.Save();
                        WpfMigration.CopyHistory(WpfMigration.OldDataDir, DataDir);
                    }
                }
                catch (Exception ex) {
                    _log.Error(ex, "Could not save the imported settings");
                }
                return imported;
            }
            return Load();
        }

        internal static AppConfig FromJson(string json) => JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();

        internal string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

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

            var json = ToJson();
            if (OperatingSystem.IsWindows()) {
                // %APPDATA% is already private to the user.
                File.WriteAllText(ConfigPath, json);
                return;
            }

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
        }
    }
}
