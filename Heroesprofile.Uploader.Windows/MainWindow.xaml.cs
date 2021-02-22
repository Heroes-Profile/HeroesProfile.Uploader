using Heroesprofile.Uploader.Windows.Properties;

using Squirrel;

using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace Heroesprofile.Uploader.Windows
{
    public partial class MainWindow : Window
    {
        public App App { get { return Application.Current as App; } }
        private bool ShutdownOnClose = true;

        public MainWindow()
        {
            InitializeComponent();

            Twitch_Extension_Checkbox.Checked += Twitch_Extension_Checkbox_Checked;
            Twitch_Extension_Checkbox.Unchecked += Twitch_Extension_Checkbox_Unchecked;
        }

        private void Twitch_Extension_Checkbox_Checked(object sender, RoutedEventArgs e)
        {
            App.Manager.TwitchExtension = true;
        }

        private void Twitch_Extension_Checkbox_Unchecked(object sender, RoutedEventArgs e)
        {
            App.Manager.TwitchExtension = false;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (App.Settings.MinimizeToTray && WindowState == WindowState.Minimized) {
                App.TrayIcon.Visible = true;
                ShutdownOnClose = false;
                Close();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            App.mainWindow = null;
            if (ShutdownOnClose) {
                App.Shutdown();
            }
        }

        private void Logo_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Process.Start("https://www.heroesprofile.com/");
        }

        private void ShowLog_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", $@"{App.SettingsDir}\logs");
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow() { Owner = this, DataContext = this };
            settings.ShowDialog();

            Twitch_Extension_Checkbox.IsEnabled = Settings.Default.HPTwitchValidated;
        }

        private async void Restart_Click(object sender, RoutedEventArgs e)
        {
            // Actually this should never happen when squirrel is disabled
#pragma warning disable 162
            if (!App.NoSquirrel) {
                await UpdateManager.RestartAppWhenExited();
            }
#pragma warning restore 162
            App.Shutdown();
        }
    }
}
