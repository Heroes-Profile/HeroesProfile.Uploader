using Heroesprofile.Uploader.Common;
using Microsoft.Win32;
using NLog;
using Squirrel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Heroesprofile.Uploader.Windows
{
    public partial class App : Application, INotifyPropertyChanged
    {
#if DEBUG
        public const bool Debug = true;
#else
        public const bool Debug = false;
#endif

#if NOSQUIRREL
        public const bool NoSquirrel = true;
#else
        public const bool NoSquirrel = false;
#endif
        // Don't want to write converters, using this quick hack instead
        public static bool StartWithWindowsCheckboxEnabled => !NoSquirrel;

        public event PropertyChangedEventHandler PropertyChanged;

        public NotifyIcon TrayIcon { get; private set; }
        public Manager Manager { get; private set; }
        public static Properties.Settings Settings { get { return Heroesprofile.Uploader.Windows.Properties.Settings.Default; } }
        public static string AppExe { get { return Assembly.GetExecutingAssembly().Location; } }
        public static string AppDir { get { return Path.GetDirectoryName(AppExe); } }
        public static string AppFile { get { return Path.GetFileName(AppExe); } }
        public static string SettingsDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Heroesprofile"); } }
        public static UpdateManager DummyUpdateManager => new UpdateManager(@"not needed here");
        public bool UpdateAvailable
        {
            get {
                return _updateAvailable;
            }
            set {
                if (_updateAvailable == value) {
                    return;
                }
                _updateAvailable = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateAvailable)));
            }
        }
        public static Version Version { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public string VersionString
        {
            get {
                return $"v{Version.Major}.{Version.Minor}" + (Version.Build == 0 ? "" : $".{Version.Build}");
            }
        }
        public bool StartWithWindows
        {
            get {
                // todo: find a way to get shortcut name from UpdateManager instead of hardcoding it
                return File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\Heroesprofile Uploader.lnk");
            }
            set {
                if (value) {
                    DummyUpdateManager.CreateShortcutsForExecutable(AppFile, ShortcutLocation.Startup, false, "--autorun");
                } else {
                    DummyUpdateManager.RemoveShortcutsForExecutable(AppFile, ShortcutLocation.Startup);
                }
            }
        }

        public readonly Dictionary<string, string> Themes = new Dictionary<string, string> {
            { "Default", null },
            { "MetroDark", "Themes/MetroDark/MetroDark.Heroesprofile.Implicit.xaml" },
        };

        private static Logger _log = LogManager.GetCurrentClassLogger();
        private UpdateManager _updateManager;
        private bool _updateAvailable;
        private object _lock = new object();
        public MainWindow mainWindow;


        private void Application_Startup(object sender, StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            SetExceptionHandlers();
            _log.Info($"App {VersionString} started");
            if (Settings.UpgradeRequired) {
                RestoreSettings();
            }
            SetupTrayIcon();
            Manager = new Manager(new ReplayStorage($@"{SettingsDir}\replays_v3.xml"));
            // Enable collection modification from any thread
            BindingOperations.EnableCollectionSynchronization(Manager.Files, _lock);

            Manager.UploadToHotslogs = Settings.UploadToHotslogs;
            Manager.PreMatchPage = Settings.PreMatchPage;
            Manager.DeleteAfterUpload = Settings.DeleteAfterUpload;
            ApplyTheme(Settings.Theme);
            Settings.PropertyChanged += (o, ev) => {
                if (ev.PropertyName == nameof(Settings.UploadToHotslogs)) {
                    Manager.UploadToHotslogs = Settings.UploadToHotslogs;
                }
                if (ev.PropertyName == nameof(Settings.DeleteAfterUpload)) {
                    Manager.DeleteAfterUpload = Settings.DeleteAfterUpload;
                }
                if (ev.PropertyName == nameof(Settings.Theme)) {
                    ApplyTheme(Settings.Theme);
                }

                if (ev.PropertyName == nameof(Settings.PreMatchPage)) {
                    Manager.PreMatchPage = Settings.PreMatchPage;
                }
            };

            if (e.Args.Contains("--autorun") && Settings.MinimizeToTray) {
                TrayIcon.Visible = true;
            } else {
                mainWindow = new MainWindow();
                mainWindow.Show();
            }
            Manager.Start(new Monitor(), new PreMatchMonitor(), new Analyzer(), new Common.Uploader());

#pragma warning disable 162
            if (!NoSquirrel) {
                //Check for updates on startup and then every hour
                CheckForUpdates();
                new DispatcherTimer() {
                    Interval = TimeSpan.FromHours(1),
                    IsEnabled = true
                }.Tick += (_, __) => CheckForUpdates();
            }
#pragma warning restore 162
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            BackupSettings();
            TrayIcon?.Dispose();
#pragma warning disable 162
            // ReSharper disable HeuristicUnreachableCode
            if (!NoSquirrel) {
                _updateManager?.Dispose();
            }
            // ReSharper restore HeuristicUnreachableCode
#pragma warning restore 162
        }

        public void ApplyTheme(string theme)
        {
            // we will need a separate resource dictionary for themes 
            // if we intend to store someting else in App resource dictionary
            Resources.MergedDictionaries.Clear();
            Themes.TryGetValue(theme, out string resource);
            if (resource != null) {
                Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri(resource, UriKind.Relative) });
            } else {
                Resources.MergedDictionaries.Add(new ResourceDictionary() { Source = new Uri("Themes/Default/Default.xaml", UriKind.Relative) });
            }
        }

        public void Activate()
        {
            if (mainWindow != null) {
                if (mainWindow.WindowState == WindowState.Minimized) {
                    mainWindow.WindowState = WindowState.Normal;
                }
                mainWindow.Activate();
            } else {
                mainWindow = new MainWindow();
                mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                TrayIcon.Visible = false;
            }
        }

        private void SetupTrayIcon()
        {
            TrayIcon = new NotifyIcon {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
                Visible = false
            };
            TrayIcon.Click += (o, e) => {
                mainWindow = new MainWindow();
                mainWindow.Show();
                TrayIcon.Visible = false;
            };
        }

        private async void CheckForUpdates()
        {
            if (Debug || !Settings.AutoUpdate) {
                return;
            }
            try {
                if (_updateManager == null) {
                    _updateManager = await UpdateManager.GitHubUpdateManager(Settings.UpdateRepository, prerelease: Settings.AllowPreReleases);
                }
                var release = await _updateManager.UpdateApp();
                if (release != null) {
                    _log.Info($"Updating app to version {release.Version}");
                    UpdateAvailable = true;
                    BackupSettings();
                }
            }
            catch (Exception e) {
                _log.Warn(e, "Error checking for updates");
            }
        }

        /// <summary>
        /// Make a backup of our settings.
        /// Used to persist settings across updates.
        /// </summary>
        public static void BackupSettings()
        {
            Settings.Save();
            string settingsFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            string destination = $@"{SettingsDir}\last.config";
            File.Copy(settingsFile, destination, true);
        }

        /// <summary>
        /// Restore our settings backup if any.
        /// Used to persist settings across updates and upgrade settings format.
        /// </summary>
        public static void RestoreSettings()
        {
            string destFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            string sourceFile = $@"{SettingsDir}\last.config";

            if (File.Exists(sourceFile)) {
                try {
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)); // Create directory if needed
                    File.Copy(sourceFile, destFile, true);
                    Settings.Reload();
                    Settings.Upgrade();

                    if (string.IsNullOrEmpty(Settings.ApplicationVersion)) { // < v1.7
                    
                    } else {
                        var previous = Version.Parse(Settings.ApplicationVersion);

                        // custom upgrade code
                    }
                }
                catch (Exception e) {
                    _log.Error(e, "Error upgrading settings");
                }
            }

            Settings.ApplicationVersion = Version.ToString();
            Settings.UpgradeRequired = false;
            Settings.Save();
        }

        /// <summary>
        /// Log all unhandled exceptions
        /// </summary>
        private void SetExceptionHandlers()
        {
            DispatcherUnhandledException += (o, e) => LogAndDisplay(e.Exception, "dispatcher");
            TaskScheduler.UnobservedTaskException += (o, e) => LogAndDisplay(e.Exception, "task");
            AppDomain.CurrentDomain.UnhandledException += (o, e) => LogAndDisplay(e.ExceptionObject as Exception, "domain");
        }

        private void LogAndDisplay(Exception e, string type)
        {
            _log.Error(e, $"Unhandled {type} exception");
            try {
                MessageBox.Show(e.ToString(), $"Unhandled {type} exception");
            }
            catch { /* probably not gui thread */ }
        }
    }
}