﻿using Squirrel;
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
            if (Properties.Settings.Default.HPTwitchExtension) {
                Twitch_Extension_Checkbox.IsEnabled = true;
            }
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
            new SettingsWindow() { Owner = this, DataContext = this }.ShowDialog();
            if (Properties.Settings.Default.HPTwitchExtension) {
                Twitch_Extension_Checkbox.IsEnabled = true;
            } else {
                Twitch_Extension_Checkbox.IsEnabled = false;
            }
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
