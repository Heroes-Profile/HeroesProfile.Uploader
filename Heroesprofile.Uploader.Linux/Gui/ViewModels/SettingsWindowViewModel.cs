using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heroesprofile.Uploader.Common;
using System;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Linux.Gui.ViewModels
{
    /// <summary>
    /// Backs the Settings dialog: prefix (with live Accounts-folder detection), theme, webhook
    /// (format-only validation - never POSTed to test it), Twitch uploader key (with a real "Check
    /// key" round trip to the API, same as the Windows SettingsWindow), and log level. Nothing here
    /// touches config.json directly - the caller applies the result via MainWindowViewModel.ApplySettings
    /// only once the dialog closes with Save.
    /// </summary>
    public partial class SettingsWindowViewModel : ObservableObject
    {
        public static readonly string[] LogLevelLabels = { "Error", "Warning", "Info", "Debug", "Trace" };

        [ObservableProperty]
        private string prefixPath;

        [ObservableProperty]
        private string detectLineText = "";

        [ObservableProperty]
        private bool detectLineIsOk;

        [ObservableProperty]
        private bool themeIsDark;

        [ObservableProperty]
        private bool themeIsLight;

        [ObservableProperty]
        private bool themeIsSystem;

        [ObservableProperty]
        private string webhookUrl;

        [ObservableProperty]
        private string webhookStatusText = "";

        [ObservableProperty]
        private bool webhookIsOk;

        [ObservableProperty]
        private string twitchUploaderKey;

        [ObservableProperty]
        private string twitchKeyStatusText = "";

        [ObservableProperty]
        private string selectedLogLevelLabel;

        [ObservableProperty]
        private bool autoUpdate;

        /// <summary>"Allow beta updates" - same wording as the Windows SettingsWindow's checkbox.</summary>
        [ObservableProperty]
        private bool allowPreReleases;

        /// <summary>NLog level name ("Warn", not "Warning") for AppConfig/Logging.ParseLevel.</summary>
        public string SelectedLogLevel => SelectedLogLevelLabel == "Warning" ? "Warn" : SelectedLogLevelLabel;

        /// <summary>"Dark", "Light", or "System" - what MainWindowViewModel.ApplySettings writes to config.json.</summary>
        public string SelectedTheme => ThemeIsDark ? "Dark" : ThemeIsLight ? "Light" : "System";

        /// <summary>An empty webhook disables it (valid); a non-empty one must be a well-formed http(s) url.</summary>
        public bool IsWebhookValid => string.IsNullOrWhiteSpace(WebhookUrl) || WebhookIsOk;

        public SettingsWindowViewModel(AppConfig config)
        {
            prefixPath = config.Prefix ?? "";
            webhookUrl = config.WebhookUrl ?? "";
            twitchUploaderKey = config.TwitchUploaderKey ?? "";
            selectedLogLevelLabel = config.LogLevel == "Warn" ? "Warning" : (config.LogLevel ?? "Info");
            autoUpdate = config.AutoUpdate;
            allowPreReleases = config.AllowPreReleases;

            themeIsDark = config.Theme == "Dark";
            themeIsLight = config.Theme == "Light";
            themeIsSystem = !themeIsDark && !themeIsLight;

            UpdateDetectLine();
            UpdateWebhookStatus();
        }

        partial void OnPrefixPathChanged(string value)
        {
            UpdateDetectLine();
        }

        private void UpdateDetectLine()
        {
            if (string.IsNullOrWhiteSpace(PrefixPath)) {
                DetectLineIsOk = false;
                DetectLineText = "";
                return;
            }

            var accounts = WinePrefix.FindAccounts(PrefixPath);
            DetectLineIsOk = accounts != null;
            DetectLineText = accounts != null
                ? $"Accounts folder found — {accounts}"
                : "Couldn't find a Heroes of the Storm Accounts folder under that path.";
        }

        [RelayCommand]
        private void SelectDark()
        {
            ThemeIsDark = true;
            ThemeIsLight = false;
            ThemeIsSystem = false;
        }

        [RelayCommand]
        private void SelectLight()
        {
            ThemeIsDark = false;
            ThemeIsLight = true;
            ThemeIsSystem = false;
        }

        [RelayCommand]
        private void SelectSystem()
        {
            ThemeIsDark = false;
            ThemeIsLight = false;
            ThemeIsSystem = true;
        }

        partial void OnWebhookUrlChanged(string value)
        {
            UpdateWebhookStatus();
        }

        private void UpdateWebhookStatus()
        {
            var url = (WebhookUrl ?? "").Trim();
            if (url == "") {
                WebhookIsOk = false;
                WebhookStatusText = "";
                return;
            }

            // Format only - a test POST would announce a fake match to whatever is listening.
            WebhookIsOk = Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
            WebhookStatusText = WebhookIsOk ? "Looks like a valid webhook url." : "Invalid url. Must start with http:// or https://";
        }

        [RelayCommand]
        private async Task CheckTwitchKeyAsync()
        {
            if (string.IsNullOrWhiteSpace(TwitchUploaderKey)) {
                TwitchKeyStatusText = "Paste your uploader key first.";
                return;
            }
            TwitchKeyStatusText = "Checking...";
            TwitchKeyStatusText = await TwitchLiveSession.Validate(TwitchUploaderKey);
        }
    }
}
