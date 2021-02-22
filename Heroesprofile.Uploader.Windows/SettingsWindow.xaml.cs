using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Heroesprofile.Uploader.Windows
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private bool showHideButton = true;
        public SettingsWindow()
        {
            InitializeComponent();
            if (App.Settings.AllowPreReleases) {
                PreReleasePanel.Visibility = Visibility.Visible;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) {
                PreReleasePanel.Visibility = Visibility.Visible;
            }
        }

        private void Twitch_Extension_Validation_Button_Click(object sender, RoutedEventArgs e)
        {
            new TwitchExtensionValidationWindow() { Owner = this }.ShowDialog();
        }

        private void Show_HP_Api_Key_Button_Click(object sender, RoutedEventArgs e)
        {
            if (showHideButton) {
                Show_HP_Api_Key_Button.Content = "hide";
                hp_api_key_label.Content = Properties.Settings.Default.HPKey;
                showHideButton = false;
            } else {
                Show_HP_Api_Key_Button.Content = "show";
                hp_api_key_label.Content = "HP Twitch API Key";
                showHideButton = true;
            }
        }
    }
}
