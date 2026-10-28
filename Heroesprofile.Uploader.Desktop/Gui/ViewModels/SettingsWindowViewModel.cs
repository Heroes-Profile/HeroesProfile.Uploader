using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heroesprofile.Uploader.Common;
using Heroesprofile.Uploader.Desktop.Platform;
using NLog;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Desktop.Gui.ViewModels
{
    /// <summary>
    /// Backs the Settings dialog, which mirrors the WPF app's SettingsWindow: replay folder, theme,
    /// (hidden) beta updates, Twitch uploader key, and webhook. As in WPF, changes apply straight away -
    /// there's no Save/Cancel - and settings are written when the window closes. The one guard is also
    /// WPF's: an invalid webhook url can't be saved, so it blocks closing until it's fixed or cleared
    /// (unless the app is quitting - see <see cref="TryClose"/>).
    /// </summary>
    public partial class SettingsWindowViewModel : ObservableObject
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private const string InvalidWebhookMessage = "Invalid url. Must start with http:// or https://";

        public sealed class ThemeChoice
        {
            public ThemeChoice(string label, string value)
            {
                Label = label;
                Value = value;
            }

            public string Label { get; }
            public string Value { get; }
            public override string ToString() => Label;
        }

        /// <summary>WPF's two themes, plus following the desktop's light/dark setting.</summary>
        public ThemeChoice[] Themes { get; } = {
            new ThemeChoice("Light", AppConfig.LightTheme),
            new ThemeChoice("Dark", AppConfig.DarkTheme),
            new ThemeChoice("Follow system", AppConfig.SystemTheme),
        };

        /// <summary>The two main-window layouts (see AppConfig.Design): the WPF-style Default, and Lite (Theme 2).</summary>
        public ThemeChoice[] Designs { get; } = {
            new ThemeChoice("Default", AppConfig.Theme1Design),
            new ThemeChoice("Lite", AppConfig.Theme2Design),
        };

        /// <summary>A main-window size: "1.5x" and its scale.</summary>
        public sealed record SizeChoice(string Label, double Scale)
        {
            public override string ToString() => Label;
        }

        /// <summary>
        /// The main window's sizes - AppConfig.UiScales, the one list of them - and Theme 2's place for the title
        /// bar's 1x/1.5x/2x buttons.
        /// </summary>
        public SizeChoice[] Sizes { get; } = AppConfig.UiScales
            .Select(s => new SizeChoice(s.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x", s))
            .ToArray();

        private readonly MainWindowViewModel _main;

        public SettingsWindowViewModel(MainWindowViewModel main)
        {
            _main = main;
            var config = main.Config;
            replayPath = config.ReplayPath ?? "";
            selectedTheme = Array.Find(Themes, t => t.Value == config.Theme) ?? Themes[1];
            selectedDesign = Array.Find(Designs, d => d.Value == config.Design) ?? Designs[0];
            selectedSize = Array.Find(Sizes, z => z.Scale == config.UiScale) ?? Sizes[0];
            allowPreReleases = config.AllowPreReleases;
            readRanks = RankReading.IsAvailable && config.ReadRanks;
            showCaptureOutline = config.ShowCaptureOutline;
            showPreReleases = config.AllowPreReleases;
            twitchUploaderKey = config.TwitchUploaderKey ?? "";
            webhookUrl = config.WebhookUrl ?? "";
            RefreshReplayPathStatus();
        }

        // Replay folder

        public string ReplayPathHelp => Platforms.Current.ReplayPathIsWinePrefix
            ? "The Wine/Proton prefix Heroes of the Storm runs in (the folder containing drive_c), a Steam " +
              "compatdata/<appid> folder, or the Heroes of the Storm \"Accounts\" folder itself."
            : "Leave empty to use the game's default location. Set it if that folder has moved, " +
              "or to point at an \"Accounts\" folder somewhere else.";

        [ObservableProperty]
        private string replayPath;

        [ObservableProperty]
        private string replayPathStatus = "";

        [ObservableProperty]
        private bool replayPathFound;

        partial void OnReplayPathChanged(string value) => RefreshReplayPathStatus();

        /// <summary>Tell the user right away whether the folder they picked actually exists.</summary>
        private void RefreshReplayPathStatus()
        {
            var folders = ReplayFolderSetup.Resolve(Platforms.Current, ReplayPath, out var error);
            ReplayPathFound = folders != null;
            ReplayPathStatus = folders != null ? $"Found: {folders.Accounts}" : error;
        }

        /// <summary>A folder picked with Browse or "Use default" applies straight away; typed paths apply on close.</summary>
        public void SetReplayPath(string path)
        {
            ReplayPath = path ?? "";
            _main.ApplyReplayPath(ReplayPath);
        }

        [RelayCommand]
        private void UseDefaultReplayPath() => SetReplayPath("");

        // Theme and updates

        [ObservableProperty]
        private ThemeChoice selectedTheme;

        partial void OnSelectedThemeChanged(ThemeChoice value)
        {
            if (value != null) {
                _main.ApplyTheme(value.Value);
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTheme2))]
        private ThemeChoice selectedDesign;

        partial void OnSelectedDesignChanged(ThemeChoice value)
        {
            if (value != null) {
                _main.ApplyDesign(value.Value);
            }
        }

        /// <summary>This window follows the Design choice too (Theme 2's control styling).</summary>
        public bool IsTheme2 => SelectedDesign?.Value == AppConfig.Theme2Design;

        [ObservableProperty]
        private SizeChoice selectedSize;

        partial void OnSelectedSizeChanged(SizeChoice value)
        {
            if (value != null) {
                _main.ApplyUiScale(value.Scale);
            }
        }

        // Rank reading - only the Ranks build offers it (RankReading.IsAvailable)

        public bool SupportsRankReading => RankReading.IsAvailable;

        // Each OS captures differently, so each has its own notes (and only Windows has an outline to show).
        public bool IsWindows => OperatingSystem.IsWindows();
        public bool IsMacOS => OperatingSystem.IsMacOS();
        public bool IsLinux => OperatingSystem.IsLinux();

        public const string RankReadingConsent =
            "While a game's loading screen is showing, the uploader will capture the Heroes of the Storm " +
            "window and read each player's rank from it. It only looks at the game window, only during the " +
            "loading screen, and never reads or changes the game itself.\n\n" +
            "After the game, the players' ranks are sent to Heroes Profile with that game's replay.\n\n" +
            "While rank reading is in beta, the part of the loading screen showing the ten player cards is " +
            "also uploaded to Heroes Profile, to help build and test it.\n\n" +
            "Turn on rank reading?";

        [ObservableProperty]
        private bool readRanks;

        partial void OnReadRanksChanged(bool value) => _main.ApplyReadRanks(value);

        /// <summary>Windows 11 can capture without its yellow outline; this shows it anyway.</summary>
        [ObservableProperty]
        private bool showCaptureOutline;

        partial void OnShowCaptureOutlineChanged(bool value) => _main.ApplyShowCaptureOutline(value);

        /// <summary>WPF keeps "Allow beta updates" hidden unless it's already on, or Ctrl+Z is pressed.</summary>
        [ObservableProperty]
        private bool showPreReleases;

        [ObservableProperty]
        private bool allowPreReleases;

        partial void OnAllowPreReleasesChanged(bool value) => _main.Config.AllowPreReleases = value;

        public void RevealPreReleases() => ShowPreReleases = true;

        // Twitch

        [ObservableProperty]
        private string twitchUploaderKey;

        [ObservableProperty]
        private string twitchKeyStatus = "";

        [ObservableProperty]
        private bool isCheckingTwitchKey;

        partial void OnTwitchUploaderKeyChanged(string value)
        {
            _main.ApplyTwitchKey(value);
            TwitchKeyStatus = "";
        }

        [RelayCommand]
        private async Task CheckTwitchKeyAsync()
        {
            if (string.IsNullOrWhiteSpace(TwitchUploaderKey)) {
                TwitchKeyStatus = "Paste your uploader key first.";
                return;
            }
            IsCheckingTwitchKey = true;
            TwitchKeyStatus = "Checking...";
            try {
                TwitchKeyStatus = await TwitchLiveSession.Validate(TwitchUploaderKey);
            }
            finally {
                IsCheckingTwitchKey = false;
            }
        }

        [RelayCommand]
        private void OpenTwitchSettings()
        {
            try {
                Process.Start(new ProcessStartInfo("https://www.heroesprofile.com/Api/Account#twitch") { UseShellExecute = true });
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not open the Twitch settings page");
            }
        }

        // Webhook

        public const string WebhookExample =
            "{\"event\": \"prematch\", \"url\": \"https://www.heroesprofile.com/PreMatch/...\", \"content\": \"Match started — ...\", \"text\": \"Match started — ...\"}";

        [ObservableProperty]
        private string webhookUrl;

        [ObservableProperty]
        private string webhookError = "";

        partial void OnWebhookUrlChanged(string value)
        {
            if (WebhookError != "" && IsValidWebhookUrl(value)) {
                WebhookError = "";
            }
        }

        /// <summary>Empty disables the webhook, anything else has to be an http(s) url.</summary>
        public static bool IsValidWebhookUrl(string url)
        {
            url = (url ?? "").Trim();
            return url == "" ||
                (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps));
        }

        /// <summary>
        /// Called as the window closes: applies what was typed but not yet applied and writes config.json.
        /// When the user closes the window (<paramref name="canStayOpen"/>), an invalid webhook url keeps it
        /// open - returns false - so it can be fixed or cleared. When the app or the OS is shutting down the
        /// window has to go, so the rest is saved and the webhook keeps its previous value.
        /// </summary>
        public bool TryClose(bool canStayOpen)
        {
            if (IsValidWebhookUrl(WebhookUrl)) {
                _main.ApplyWebhookUrl(WebhookUrl);
            } else if (canStayOpen) {
                WebhookError = InvalidWebhookMessage;
                return false;
            } else {
                _log.Warn("Settings closed by shutdown with an invalid webhook url - keeping the previous one");
            }
            if ((ReplayPath ?? "").Trim() != (_main.Config.ReplayPath ?? "")) {
                _main.ApplyReplayPath(ReplayPath);
            }
            _main.SaveConfig();
            return true;
        }
    }
}
